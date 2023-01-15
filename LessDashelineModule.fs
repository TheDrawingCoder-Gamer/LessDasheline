namespace Celeste.Mod.LessDasheline

open Microsoft.Xna.Framework
open Monocle
open System.Reflection
open MonoMod.Utils
open MonoMod.RuntimeDetour
open Celeste
open Celeste.Mod
open System 

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

module LessDasheline = 
    let logKey: String = "LessDasheline"
    let prevDashes: FieldInfo = typeof<Player>.GetField("lastDashes", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let hairFlashTimer: FieldInfo = typeof<Player>.GetField("hairFlashTimer", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let flashTimerKey = "LessDasheline/flashTimer"
    let getLessFlash (p: Player): single = 
        use d = new DynData<Player>(p)
        try 
            d.Get<single> flashTimerKey 
        with 
            | _ -> 
                d.Set<single>(flashTimerKey, single 0)
                single 0 
    let setLessFlash (p: Player) (s: single): unit = 
        use d = new DynData<Player>(p)
        try 
            d.Set<single>(flashTimerKey, s)
        with 
            e -> 
                Logger.Log(LogLevel.Error, logKey, e.ToString())
    let changeFlashWith (p: Player) (update : single -> single): unit = 
        let cur = getLessFlash p 
        setLessFlash p (update cur)
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
            | _ -> this.GetOneDashColor(badeline)
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
    member this.Player_Update (orig : On.Celeste.Player.orig_Update) (player : Player) = 
        if not this.settings.Enabled || player.GetType().Name = "Ghost" then
            ()
        elif (player.Dashes < 3 && not this.settings.DoNormalDashCounts && not (this.IsDashCountOverridden(player.Dashes))) then
            player.OverrideHairColor <- Nullable()
        else
            use data = new DynData<Player>(player)
            let justDashed = 
                try 
                    data.Get<bool>("LessDasheline/justDashed")
                with 
                    _ -> false 
            if (justDashed) then
                data.Set<bool>("LessDasheline/justDashed", false)
            // let data = new DynData<Player>(player)
            // use custom field to step around more dasheline
            let flashTimer(): single = LessDasheline.getLessFlash player
            // let hair renderer do it
            if (player.StateMachine.State = Player.StStarFly) then
               player.OverrideHairColor <- Nullable() 
            elif (justDashed) then
                LessDasheline.setLessFlash player (single 0.12)
            elif (flashTimer() > single 0.0) then 
                player.OverrideHairColor <- Nullable(Player.FlashHairColor)
                LessDasheline.changeFlashWith player (fun s -> s - Engine.DeltaTime)
            else 
                player.OverrideHairColor <- Nullable(this.GetWigColor player player.Dashes)
        orig.Invoke(player)
    member this.hook_PlayerUpdate = On.Celeste.Player.hook_Update this.Player_Update
    member this.Player_GetTrailColor (orig : On.Celeste.Player.orig_GetTrailColor) (player : Player) (wasDashB : bool) = 
        if not this.settings.Enabled then 
            orig.Invoke(player, wasDashB)
        else 
            use data = new DynData<Player>(player)
            try 
                let dashes = data.Get<int>("LessDasheline/startDashCount")
                this.GetWigColor player (dashes - 1)
            with 
                _ -> 
                    data.Set<int>("LessDashline/startDashCount", if wasDashB then 2 else 1)
                    orig.Invoke(player, wasDashB)

    member this.hook_GetTrailColor = On.Celeste.Player.hook_GetTrailColor this.Player_GetTrailColor
    member this.Player_StartDash (orig : On.Celeste.Player.orig_StartDash) (player : Player) = 
        use data = new DynData<Player>(player)
        data.Set<int>("LessDasheline/startDashCount", player.Dashes)
        data.Set<bool>("LessDasheline/justDashed", true)
        orig.Invoke(player)

    member this.hook_StartDash = On.Celeste.Player.hook_StartDash this.Player_StartDash
    member this.Player_ReflectionFallBegin (orig : On.Celeste.Player.orig_ReflectionFallBegin) (player : Player) = 
        let data = new DynData<Player>(player)
        data.Set<int>("LessDasheline/startDashCount", 2)
        orig.Invoke(player)

    member this.hook_ReflectionFallBegin = On.Celeste.Player.hook_ReflectionFallBegin this.Player_ReflectionFallBegin
    override this.Load() =
        using (new DetourContext(Before = ResizeArray<string> ["*"] )) ( fun _ -> 
            On.Celeste.Player.add_Update this.hook_PlayerUpdate
        )
        using (new DetourContext(After = ResizeArray<string> ["*"])) ( fun _ -> 
            On.Celeste.Player.add_GetTrailColor this.hook_GetTrailColor
        )
        On.Celeste.Player.add_StartDash this.hook_StartDash
        On.Celeste.Player.add_ReflectionFallBegin this.hook_ReflectionFallBegin
    override this.Unload() = 
        On.Celeste.Player.remove_Update this.hook_PlayerUpdate
        On.Celeste.Player.remove_GetTrailColor this.hook_GetTrailColor

                

