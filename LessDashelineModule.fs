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
    let prevDashes: FieldInfo = typeof<Player>.GetField("lastDashes", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let hairFlashTimer: FieldInfo = typeof<Player>.GetField("hairFlashTimer", BindingFlags.Instance ||| BindingFlags.NonPublic)
type LessDashelineModule() = 
    inherit EverestModule()
    do 
        Logger.SetLogLevel("LessDashelineModule", LogLevel.Info)

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
    member this.GetWigColor(player : Player) (dashes : int) = 
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
            let lastDashes: int = unbox (LessDasheline.prevDashes.GetValue(player))
            // let data = new DynData<Player>(player)
            // use custom field to step around more dasheline
            let flashTimer: float32 = unbox (LessDasheline.hairFlashTimer.GetValue player)

            // let hair renderer do it
            if (player.StateMachine.State = Player.StStarFly) then
               player.OverrideHairColor <- Nullable() 
            elif (lastDashes <> player.Dashes) then
                LessDasheline.hairFlashTimer.SetValue(player, box (float32 12.0))
            elif (flashTimer > float32 0.0) then 
                player.OverrideHairColor <- Nullable(Player.FlashHairColor)
                LessDasheline.hairFlashTimer.SetValue(player, box (flashTimer - Engine.DeltaTime))
            else 
                player.OverrideHairColor <- Nullable(this.GetWigColor player player.Dashes)
        orig.Invoke(player)
    member this.hook_PlayerUpdate = On.Celeste.Player.hook_Update this.Player_Update
    member this.Player_GetTrailColor (orig : On.Celeste.Player.orig_GetTrailColor) (player : Player) (wasDashB : bool) = 
        if not this.settings.Enabled then 
            orig.Invoke(player, wasDashB)
        else 
            let data = new DynData<Player>(player)
            try 
                let dashes = data.Get<int>("LessDasheline/startDashCount")
                this.GetWigColor player (dashes - 1)
            with 
                _ -> 
                    data.Set<int>("LessDashline/startDashCount", if wasDashB then 2 else 1)
                    orig.Invoke(player, wasDashB)

    member this.hook_GetTrailColor = On.Celeste.Player.hook_GetTrailColor this.Player_GetTrailColor
    member this.Player_StartDash (orig : On.Celeste.Player.orig_StartDash) (player : Player) = 
        let data = new DynData<Player>(player)
        data.Set<int>("LessDasheline/startDashCount", player.Dashes)
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

                

