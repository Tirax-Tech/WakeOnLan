namespace TiraxTech.WakeOnLan.Controllers

open Microsoft.AspNetCore.Mvc
open TiraxTech.WakeOnLan

[<ApiController>]
[<Route("[controller]")>]
type WolController() =
    inherit ControllerBase()
    
    [<HttpPost>]
    member _.Post(mac :string) =
        mac |> WOL.wakeOnLan |> Async.StartImmediateAsTask