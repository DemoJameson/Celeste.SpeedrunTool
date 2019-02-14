using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class ZipMoverAction : AbstractEntityAction
    {
        private Dictionary<EntityID, ZipMover> _savedZipMovers = new Dictionary<EntityID, ZipMover>();

        public override void OnQuickSave(Level level)
        {
            _savedZipMovers = level.Tracker.GetDictionary<ZipMover>();
        }

        private void RestoreZipMoverPosition(On.Celeste.ZipMover.orig_ctor_EntityData_Vector2 orig, ZipMover self,
            EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedZipMovers.ContainsKey(entityId))
            {
                self.Position = _savedZipMovers[entityId].Position;
            }
        }

        public override void OnClear()
        {
            _savedZipMovers.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.ZipMover.ctor_EntityData_Vector2 += RestoreZipMoverPosition;
            On.Celeste.ZipMover.Sequence += ZipMoverOnSequence;
        }

        private IEnumerator ZipMoverOnSequence(On.Celeste.ZipMover.orig_Sequence orig, ZipMover self)
        {
            if (SpeedrunToolModule.Settings.Enabled)
            {
                Vector2 start = (Vector2) self.GetPrivateField("start");
                Vector2 target = (Vector2) self.GetPrivateField("target");
                SoundSource soundSource = self.GetPrivateField("sfx") as SoundSource;
                Sprite streetlight = self.GetPrivateField("streetlight") as Sprite;

                Vector2 currentPosition = self.Position;
                float goProgress = 0f;
                float backProgress = 0f;
                int audioTime = 0;

                EntityID entityId = self.GetEntityId();
                if (IsLoadStart && _savedZipMovers.ContainsKey(entityId))
                {
                    ZipMover savedZipMover = _savedZipMovers[entityId];
                    goProgress = savedZipMover.GetExtendedDataValue<float>(nameof(goProgress));
                    backProgress = savedZipMover.GetExtendedDataValue<float>(nameof(backProgress));
                    audioTime = savedZipMover.GetExtendedDataValue<int>(nameof(audioTime));
                }

                while (true)
                {
                    while (!self.HasPlayerRider() && currentPosition == start|| IsLoadStart || IsLoading)
                        yield return null;

                    DateTime startTime = DateTime.Now;
                    soundSource.Play("event:/game/01_forsaken_city/zip_mover");
                    if (audioTime > 0 && goProgress >= 1)
                    {
                        soundSource.SetTime(audioTime);
                    }
                    else
                    {
                        goProgress = 0;
                    }

                    Input.Rumble(RumbleStrength.Medium, RumbleLength.Short);
                    self.StartShaking(0.1f);
                    yield return 0.1f;

                    if (goProgress < 1)
                    {
                        streetlight.SetAnimationFrame(3);
                    }
                    else
                    {
                        streetlight.SetAnimationFrame(2);
                    }

                    self.StopPlayerRunIntoAnimation = false;
                    while (goProgress < 1.0)
                    {
                        yield return null;

                        goProgress = Calc.Approach(goProgress, 1f, 2f * Engine.DeltaTime);
                        self.SetExtendedDataValue(nameof(goProgress), goProgress);
                        audioTime = (int) (DateTime.Now - startTime).TotalMilliseconds - 100;
                        self.SetExtendedDataValue(nameof(audioTime), audioTime);

                        self.SetPrivateField("percent", Ease.SineIn(goProgress));
                        float percent = (float) self.GetPrivateField("percent");
                        Vector2 to = Vector2.Lerp(currentPosition, target, percent);
                        self.GetPrivateMethod("ScrapeParticlesCheck").Invoke(self, new object[] {to});
                        if (self.Scene.OnInterval(0.1f))
                        {
                            object pathRenderer = self.GetPrivateField("pathRenderer");
                            pathRenderer.GetType().GetMethod("CreateSparks").Invoke(pathRenderer, new object[] { });
                        }

                        self.MoveTo(to);
                    }

                    self.StartShaking(0.2f);
                    Input.Rumble(RumbleStrength.Strong, RumbleLength.Medium);
                    self.SceneAs<Level>().Shake();
                    self.StopPlayerRunIntoAnimation = true;

                    yield return 0.5f;

                    self.StopPlayerRunIntoAnimation = false;
                    streetlight.SetAnimationFrame(2);
                    while (backProgress < 1.0)
                    {
                        yield return null;

                        backProgress = Calc.Approach(backProgress, 1f, 0.5f * Engine.DeltaTime);
                        self.SetExtendedDataValue(nameof(backProgress), backProgress);
                        audioTime = (int) (DateTime.Now - startTime).TotalMilliseconds - 600;
                        self.SetExtendedDataValue(nameof(audioTime), audioTime);

                        self.SetPrivateField("percent", 1f - Ease.SineIn(backProgress));
                        Vector2 to = Vector2.Lerp(target, start, Ease.SineIn(backProgress));
                        self.MoveTo(to);
                    }

                    self.StopPlayerRunIntoAnimation = true;
                    self.StartShaking(0.2f);
                    streetlight.SetAnimationFrame(1);

                    // reset
                    currentPosition = start;
                    goProgress = 0.0f;
                    backProgress = 0.0f;
                    audioTime = 0;
                    self.SetExtendedDataValue(nameof(goProgress), goProgress);
                    self.SetExtendedDataValue(nameof(backProgress), backProgress);
                    self.SetExtendedDataValue(nameof(audioTime), audioTime);

                    yield return 0.5f;
                }
            }
            else
            {
                yield return orig(self);
            }
        }

        public override void OnUnload()
        {
            On.Celeste.ZipMover.ctor_EntityData_Vector2 -= RestoreZipMoverPosition;
            On.Celeste.ZipMover.Sequence -= ZipMoverOnSequence;
        }

        public override void OnInit() => typeof(ZipMover).AddToTracker();
    }
}