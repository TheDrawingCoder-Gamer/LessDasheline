namespace Celeste.Mod.LessDasheline

open Microsoft.Xna.Framework
open Monocle
open System.Reflection
open MonoMod.Utils
open MonoMod.RuntimeDetour
open Celeste
open Celeste.Mod
open System 
open System.Collections.Generic
type LessDashelineModuleSettings() = 
    inherit EverestModuleSettings()

    member val Enabled: bool = true with get, set
    
    [<SettingSubText("Use map overrides from More Dasheline")>]
    member val UseMapOverrides: bool = true with get, set
    
    [<SettingName("Apply Low Dash Settings"); SettingSubText("Applies Settings for 0, 1, 2")>]
    member val DoNormalDashCounts: bool = false with get, set

    member val UsedDashColor: string = "44b7ff" with get, set
    member val OneDashColor: string = "ac3232" with get, set
    member val TwoDashColor: string = "ff6def" with get, set
    member val ThreeDashColor: string = "00f000" with get, set
    member val FourDashColor: string = "0000ff" with get, set
    member val FiveDashColor: string = "ff00ff" with get, set
    member val SixDashColor: string = "ffff00" with get, set

    member val FlashColor: string = "ffffff" with get, set


    [<SettingSubText("Plays a small color fade upon regaining dashes")>]
    member val DoRecharge: bool = false with get, set

    [<SettingIgnore>]
    member val ExtraColors: Dictionary<int, string> = new Dictionary<_, _>() with get, set

module LessDasheline = 
    let logKey: string = "LessDasheline"
    let prevDashes: FieldInfo = typeof<Player>.GetField("lastDashes", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let hairFlashTimer: FieldInfo = typeof<Player>.GetField("hairFlashTimer", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let deadBodyHairColor: FieldInfo = typeof<PlayerDeadBody>.GetField("initialHairColor", BindingFlags.Instance ||| BindingFlags.NonPublic)

    let flashTimerKey = "LessDasheline/flashTimer"

    let getDynField<'T, 'O when 'O : not struct> (o: 'O) (key : string): option<'T> =
        use d = new DynData<'O>(o)
        try 
            Some (d.Get<'T> key)
        with 
            | _ -> 
                None
    let getDynFieldOrElse<'T, 'O when 'O : not struct> (o: 'O) (key: string) (orElse: unit -> 'T) : 'T = 
        Option.defaultWith orElse (getDynField o key)
    let setDynField<'T, 'O when 'O : not struct> (o: 'O) (key : string) (value: 'T): bool = 
        use d = new DynData<'O>(o)
        try 
            d.Set<'T>(key, value)
            true 
        with
            e -> 
                Logger.Log(LogLevel.Error, logKey, "Error while setting dyn field!")
                Logger.LogDetailed(e)
                false
    let updateDynField<'T, 'O when 'O: not struct> (o: 'O) (key : string) (orElse : unit -> 'T) (modifier : 'T -> 'T): bool =
        let cur = getDynFieldOrElse o key orElse 
        setDynField o key (modifier cur)
    let getLessFlash (p: Player) = getDynFieldOrElse<single, Player> p flashTimerKey (fun () -> single 0)
    let setLessFlash (p: Player) = setDynField<single, Player> p flashTimerKey 
    let changeFlashWith (p: Player) = updateDynField<single, Player> p flashTimerKey (fun () -> single 0) 
    [<Literal>]
    let ChargeTimerMax: single = 0.12f
open LessDasheline
type LessDashelineModule() = 
    inherit EverestModule()
    do 
        #if RELEASE
        Logger.SetLogLevel(logKey, LogLevel.Info)
        #else 
        Logger.SetLogLevel(logKey, LogLevel.Verbose)
        #endif
    override this.SettingsType: Type = typeof<LessDashelineModuleSettings>
    
    member this.settings: LessDashelineModuleSettings = this._Settings :?> _
    
    member this.IsDashCountOverridden(dashCount : int) = 
        if (this.settings.UseMapOverrides) then
            match Engine.Scene with 
            | :? Level as l -> l.Session.GetCounter("moreDasheline_haircolor" + dashCount.ToString() + "Set") = 1
            | _ -> false
        else
            false
    member this.WasDashB (player : Player) (dashCount : int) = 
        match dashCount with 
        | 0 -> false
        | 1 -> false
        | d -> player.MaxDashes = d
    member this.GetVanillaDashColor (settingString : string) (badColor : Color) (normalColor : Color) (badeline : bool): Color = 
        if (this.settings.DoNormalDashCounts) then
            Calc.HexToColor(settingString)
        elif (badeline) then
            badColor
        else
            normalColor
    member this.GetOneDashColor = this.GetVanillaDashColor this.settings.OneDashColor Player.NormalBadelineHairColor Player.NormalHairColor
    member this.GetUsedDashColor = this.GetVanillaDashColor this.settings.UsedDashColor Player.UsedBadelineHairColor Player.UsedHairColor
    member this.GetTwoDashColor = this.GetVanillaDashColor this.settings.TwoDashColor Player.TwoDashesBadelineHairColor Player.TwoDashesHairColor
    member this.GetDashColorNoOverrides (player : Player) (dashCount : int) (badeline : bool) = 

        match dashCount with 
            | 0 -> 
                if player.MaxDashes = 0 then 
                    this.GetOneDashColor badeline 
                else 
                    this.GetUsedDashColor badeline
            | 2 -> this.GetTwoDashColor(badeline)
            | 3 -> Calc.HexToColor(this.settings.ThreeDashColor : string)
            | 4 -> Calc.HexToColor(this.settings.FourDashColor : string)
            | 5 -> Calc.HexToColor(this.settings.FiveDashColor : string)
            | 6 -> Calc.HexToColor(this.settings.SixDashColor : string)
            | 1 -> this.GetOneDashColor(badeline)
            | d -> 
                if this.settings.ExtraColors.ContainsKey d then 
                    this.settings.ExtraColors.Item d |> Calc.HexToColor
                else 
                    this.GetOneDashColor(badeline)

    member this.GetDashColor (player: Player) (dashCount : int) (badeline : bool) = 
        let defColor = this.GetDashColorNoOverrides player dashCount badeline
        if (not this.settings.UseMapOverrides) then  
            defColor
        else 
            let flag = "moreDasheline_haircolor" + dashCount.ToString()
            match Engine.Scene with 
            | :? Level as l ->
                let s = l.Session
                if s.GetCounter(flag + "Set") = 0 then 
                    defColor
                else 
                    let mutable retColor = Calc.HexToColor(s.GetCounter(flag) : int)
                    retColor.A <- byte (s.GetCounter (flag + "Alpha"))
                    retColor
            | _ -> defColor

    member this.GetWigColor (player : Player) (dashes : int) = 
        let badeline = 
            match player with 
            | null -> false 
            | p -> p.Sprite.Mode = PlayerSpriteMode.MadelineAsBadeline
        this.GetDashColor player dashes badeline

    member this.Player_UpdateHair (orig : On.Celeste.Player.orig_UpdateHair) (player : Player) (applyGravity: bool) = 
        if this.settings.Enabled && player.GetType().Name <> "Ghost" then
            // use custom field to step around more dasheline
            let flashTimer: single = LessDasheline.getLessFlash player
            let weave = this.GetWigColor player player.Dashes
            let lastDashes: int = prevDashes.GetValue(player) |> unbox
            let chargeTimer = getDynFieldOrElse player "LessDasheline/rechargeTimer" (fun () -> 0f)
            let refillAt = getDynFieldOrElse player "LessDasheline/rechargeAt" (fun () -> -1l)
            let refillInto = getDynFieldOrElse player "LessDasheline/rechargeInto" (fun () -> -1l)
            // let hair renderer do it
            if (player.StateMachine.State = Player.StStarFly) then
               player.OverrideHairColor <- Nullable() 
            elif player.Dashes = 0 && player.Dashes < player.MaxDashes then 
                player.OverrideHairColor <- Nullable(Color.Lerp(player.Hair.Color, weave, 6f * Engine.DeltaTime))
            elif lastDashes < player.Dashes && this.settings.DoRecharge then 
                setDynField player "LessDasheline/rechargeTimer" ChargeTimerMax |> ignore
                setDynField player "LessDasheline/rechargeAt" lastDashes |> ignore
                setDynField player "LessDasheline/rechargeInto" player.Dashes |> ignore 
            elif lastDashes > player.Dashes || (lastDashes < player.Dashes && not this.settings.DoRecharge) then
                LessDasheline.setLessFlash player 0.12f |> ignore
            elif (flashTimer > 0.0f) then 
                player.OverrideHairColor <- Nullable(Calc.HexToColor(this.settings.FlashColor))
                LessDasheline.changeFlashWith player (fun s -> s - Engine.DeltaTime) |> ignore
            elif chargeTimer > 0f && refillInto = player.Dashes then  
                let lowWig = this.GetWigColor player refillAt 
                let highWig = this.GetWigColor player refillInto
                player.OverrideHairColor <- Nullable(Color.Lerp(highWig, lowWig, chargeTimer / ChargeTimerMax))
                updateDynField<single, Player> player "LessDasheline/rechargeTimer" (fun () -> 0f) (fun s -> s - Engine.DeltaTime) |> ignore
            else
                player.OverrideHairColor <- Nullable(weave)
        orig.Invoke(player, applyGravity)
    member this.hook_Player_UpdateHair = On.Celeste.Player.hook_UpdateHair this.Player_UpdateHair
    member this.Player_GetTrailColor (orig : On.Celeste.Player.orig_GetTrailColor) (player : Player) (wasDashB : bool) = 
        if not this.settings.Enabled then 
            orig.Invoke(player, wasDashB)
        else 
            match getDynField<int, Player> player "LessDasheline/startDashCount" with 
            | Some(dashes) -> this.GetWigColor player (dashes - 1)
            | None -> 
                setDynField player "LessDasheline/startDashCount" (if wasDashB then 2 else 1) |> ignore
                orig.Invoke(player,wasDashB)
    member this.hook_GetTrailColor = On.Celeste.Player.hook_GetTrailColor this.Player_GetTrailColor

    member this.Player_StartDash (orig : On.Celeste.Player.orig_StartDash) (player : Player) = 
        use data = new DynData<Player>(player)
        data.Set<int>("LessDasheline/startDashCount", player.Dashes)
        let res = orig.Invoke(player)
        if this.settings.Enabled then 
            data.Set<bool>("wasDashB", this.WasDashB player player.Dashes)
        res
    member this.hook_StartDash = On.Celeste.Player.hook_StartDash this.Player_StartDash

    member this.Player_ReflectionFallBegin (orig : On.Celeste.Player.orig_ReflectionFallBegin) (player : Player) = 
        use data = new DynData<Player>(player)
        data.Set<int>("LessDasheline/startDashCount", 2)
        orig.Invoke(player)
    member this.hook_ReflectionFallBegin = On.Celeste.Player.hook_ReflectionFallBegin this.Player_ReflectionFallBegin

    member this.Player_Die (orig : On.Celeste.Player.orig_Die) (player: Player) (dir: Vector2) (evenIfInvincible: bool) (registerDeathInStats: bool) = 
        let newDeadBody = orig.Invoke(player, dir, evenIfInvincible, registerDeathInStats)
        if this.settings.Enabled then 
            LessDasheline.deadBodyHairColor.SetValue(newDeadBody, this.GetWigColor player player.MaxDashes)
        newDeadBody
    member this.hook_Player_Die = On.Celeste.Player.hook_Die this.Player_Die

    override this.Load() =
        using (new DetourContext(After = ResizeArray<string> ["*"])) ( fun _ -> 
            On.Celeste.Player.add_GetTrailColor this.hook_GetTrailColor
        )
        On.Celeste.Player.add_UpdateHair this.hook_Player_UpdateHair
        On.Celeste.Player.add_StartDash this.hook_StartDash
        On.Celeste.Player.add_ReflectionFallBegin this.hook_ReflectionFallBegin
        On.Celeste.Player.add_Die this.hook_Player_Die
    override this.Unload() = 
        On.Celeste.Player.remove_UpdateHair this.hook_Player_UpdateHair
        On.Celeste.Player.remove_GetTrailColor this.hook_GetTrailColor

                

