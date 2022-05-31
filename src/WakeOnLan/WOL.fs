module TiraxTech.WakeOnLan.WOL

#nowarn "760"  // 760 = "using new for ownership"

open System
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Text.RegularExpressions
open RZ.FSharp.Extension

// Ref: https://stackoverflow.com/questions/861873/wake-on-lan-using-c-sharp

[<AutoOpen>]
module Helpers =
    let buildMagicPacket mac_address =
        let normalized = Regex.Replace(mac_address, "[: -]", "")
        let mac_bytes = Convert.FromHexString normalized
        in seq {
            for _ in 1..6 -> 0xFFuy
            for _ in 1..16 do yield! mac_bytes
        }
        
    let getIPv4UnicastAddress (interface_properties :IPInterfaceProperties) =
        if interface_properties.GetIPv4Properties().IsAutomaticPrivateAddressingActive then
            None
        else
            interface_properties.UnicastAddresses
            |> Seq.filter (fun u -> u.Address.AddressFamily = AddressFamily.InterNetwork)
            |> Seq.map (fun u -> u.Address)
            |> Seq.tryHead
        
    let getIPv6UnicastAddress (interface_properties :IPInterfaceProperties) =
        interface_properties.UnicastAddresses
        |> Seq.filter (fun u -> u.Address.AddressFamily = AddressFamily.InterNetworkV6 && not u.Address.IsIPv6LinkLocal)
        |> Seq.map (fun u -> u.Address)
        |> Seq.tryHead
        
    type SendResult = {
        MulticastIP:string
        UnicastIP :string
        Result     :bool
    }
        
    let [<Literal>] WakeOnLanPort = 9 // Is it?
    let sendWakeOnLan (magic_paket :byte[]) (multicast_ip :IPAddress) (unicast_local_ip :IPAddress) = async {
        printfn $"Send WOL from {unicast_local_ip} to {multicast_ip}"
        use client = UdpClient(IPEndPoint(unicast_local_ip, 0))
        let! sent = client.SendAsync(magic_paket, magic_paket.Length, IPEndPoint(multicast_ip, WakeOnLanPort)) |> Async.AwaitTask
        return { MulticastIP = multicast_ip.ToString()
                 UnicastIP   = unicast_local_ip.ToString()
                 Result      = sent = magic_paket.Length }
    }
    
    let [<Literal>] IPv6MulticastPrefix (* with zone index *) = "FF02::1%"
    let [<Literal>] IPv4Multicast = "224.0.0.1"
    let broadcastWakeOnLan (magic_packet :byte[]) (network :IPInterfaceProperties) =
        let ipv6_unicast_address = network |> getIPv6UnicastAddress
        let ipv4_unicast_address = network |> getIPv4UnicastAddress
        let multicast_addresses = network.MulticastAddresses |> Seq.map (fun a -> a.Address)
        
        let getUnitcastIp (multicast_ip :IPAddress) =
            let ip_text = multicast_ip.ToString()
            in if ip_text.StartsWith(IPv6MulticastPrefix, StringComparison.OrdinalIgnoreCase)
               then ipv6_unicast_address
               elif ip_text = IPv4Multicast then ipv4_unicast_address
               else None
               
        multicast_addresses
        |> Seq.choose (fun ip -> ip |> getUnitcastIp |> Option.map (fun unicast_ip -> ip, unicast_ip))
        |> Seq.map (fun (ip, unicast_ip) -> unicast_ip |> sendWakeOnLan magic_packet ip)
        |> Async.Parallel

let wakeOnLan mac_address =
    let magic_packet = mac_address |> buildMagicPacket |> Seq.toArray
    
    let available_networks =
        NetworkInterface.GetAllNetworkInterfaces()
        |> Seq.filter (fun n -> n.NetworkInterfaceType <> NetworkInterfaceType.Loopback && n.OperationalStatus = OperationalStatus.Up)
        |> Seq.map (fun n -> n.GetIPProperties())
        
    let send_results = available_networks
                       |> Seq.map (broadcastWakeOnLan magic_packet)
                       |> Async.Sequential
    in async {
        let! result = send_results
        return result |> Seq.collect id
    }