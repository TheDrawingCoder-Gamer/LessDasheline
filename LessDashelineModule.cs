using System;
using Microsoft.Xna.Framework;
using Monocle;
using System.Reflection;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
namespace Celeste.Mod.LessDasheline {
    public class LessDashelineModule : EverestModule {
        public static LessDashelineModule Instance { get; private set; }

        public override Type SettingsType => typeof(LessDashelineModuleSettings);
        public static LessDashelineModuleSettings Settings => (LessDashelineModuleSettings) Instance._Settings;

        
        private FieldInfo prevDashes = typeof(Player).GetField("lastDashes", BindingFlags.Instance | BindingFlags.NonPublic);

        public LessDashelineModule() {
            Instance = this;
#if DEBUG
            // debug builds use verbose logging
            Logger.SetLogLevel(nameof(LessDashelineModule), LogLevel.Verbose);
#else
            // release builds use info logging to reduce spam in log files
            Logger.SetLogLevel(nameof(LessDashelineModule), LogLevel.Info);
#endif
        }
        private static Color GetDashColor(int dashCount, bool badeline) {
            switch (dashCount) {
                case 0: 
                    if (Engine.Scene is Level l && l.Session.Inventory.Dashes == 0) {
                        return badeline ? Player.NormalBadelineHairColor : Player.NormalHairColor;
                    } else {
                        return badeline ? Player.UsedBadelineHairColor : Player.UsedHairColor;
                    }
                case 2: 
                    if (badeline)
                        return Player.TwoDashesBadelineHairColor;
                    else 
                        return Player.TwoDashesHairColor;
                case 3: 
                    return Calc.HexToColor(Settings.ThreeDashColor);
                case 4: 
                    return Calc.HexToColor(Settings.FourDashColor);
                case 5: 
                    return Calc.HexToColor(Settings.FiveDashColor);
                default: 
                    return badeline ? Player.NormalBadelineHairColor : Player.NormalHairColor;

            }
        }
        private static Color GetWigColor(Player player, int dashes) {
            bool badeline = player != null && player.Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline;
            return GetDashColor(dashes, badeline);
        }
        private void PlayerUpdate(On.Celeste.Player.orig_Update orig, Player self) {
            // we are disabled, don't do anything - let other mods/base game handle it
            if (!Settings.Enabled) {
               orig(self);
               return;
            }
            if (self.Dashes < 3) {
               
               self.OverrideHairColor = null;
               orig(self);
               return;
            } 
            int lastDashes = (int) prevDashes.GetValue(self);
            DynData<Player> data = new DynData<Player>(self);
            float flashTimer = 0f;
            try {
              flashTimer = data.Get<float>("LessDasheline/hairFlashTimer");
            } catch {}
            if (lastDashes != self.Dashes) {
               data.Set<float>("LessDasheline/hairFlashTimer", 0.12f); 
            }
            if (flashTimer > 0f) {
               self.OverrideHairColor = Player.FlashHairColor;
               data.Set<float>("LessDasheline/hairFlashTimer", flashTimer - Engine.DeltaTime);
            } else if (self.Sprite.CurrentAnimationID.ToLower().Contains("starfly")) {
               self.OverrideHairColor = null;
            } else {
               self.OverrideHairColor = GetWigColor(self, self.Dashes);            
            }
            orig(self);
        }
        private int Player_StartDash(On.Celeste.Player.orig_StartDash orig, Player self) {
            DynData<Player> data = new DynData<Player>(self);
            data.Set<int>("LessDasheline/startDashCount", self.Dashes);
            return orig(self);
        }
        private void Player_ReflectionFallStart(On.Celeste.Player.orig_ReflectionFallBegin orig, Player self) {
            DynData<Player> data = new DynData<Player>(self);
            data.Set<int>("LessDasheline/startDashCount", 2);

        }
        private Color Player_GetTrailColor(On.Celeste.Player.orig_GetTrailColor orig, Player self, bool wasDashB) {
            if (!Settings.Enabled) 
                return orig(self, wasDashB);
            DynData<Player> data = new DynData<Player>(self);
            try {
                int dashes = data.Get<int>("LessDasheline/startDashCount");
                Color wigColor = GetWigColor(self, dashes - 1);
                Logger.Log(LogLevel.Info, "LessDasheline", $"Wig level ${wigColor.ToString()}");
                return GetWigColor(self, dashes - 1);
            } catch {
                data.Set<int>("LessDasheline/startDashCount", wasDashB ? 2 : 1); 
                return orig(self, wasDashB); 
            }
        }
        public override void Load() {
            using (new DetourContext { Before = {"*"}}) { 
                On.Celeste.Player.Update += PlayerUpdate;
            }
            using (new DetourContext { After = { "*" }}) {
                On.Celeste.Player.GetTrailColor += Player_GetTrailColor;
            }
            On.Celeste.Player.StartDash += Player_StartDash;
            On.Celeste.Player.ReflectionFallBegin += Player_ReflectionFallStart;
        }

        public override void Unload() {
            On.Celeste.Player.Update -= PlayerUpdate;
            On.Celeste.Player.GetTrailColor -= Player_GetTrailColor;
        }
    }
}
