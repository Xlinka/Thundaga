using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BaseX;
using FrooxEngine;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using UnityNeos;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using ParticleSystem = FrooxEngine.ParticleSystem;
using ThreadPriority = System.Threading.ThreadPriority;

namespace Thundaga
{
    public class UpdateLoop
    {
        public static bool shutdown;

        public static void Update()
        {
            var dateTime = DateTime.UtcNow;
            const float tickRate = 1f / 60f;
            while (!shutdown)
            {
                Engine.Current.RunUpdateLoop();
                PacketManager.FinishNeosQueue();
                dateTime = dateTime.AddTicks((long) (10000000.0 * tickRate));
                var timeSpan = dateTime - DateTime.UtcNow;
                if (timeSpan.TotalMilliseconds > 0.0)
                {
                    var totalMilliseconds = (int) timeSpan.TotalMilliseconds;
                    if (totalMilliseconds > 0)
                        Thread.Sleep(totalMilliseconds);
                }
                else
                    dateTime = DateTime.UtcNow;
            }
        }
    }

    [HarmonyPatch(typeof(FrooxEngineRunner))]
    public static class FrooxEngineRunnerPatch
    {
        private static bool _startedUpdating;
        private static int _lastDiagnosticReport = 1800;
        private static IntPtr? _renderThreadPointer;
        private static bool _refreshAllConnectors;

        private static void RefreshAllConnectors()
        {
            var count = 0;
            foreach (var component in 
                     from world in Engine.Current.WorldManager.Worlds.ToList()
                     from slot in world.AllSlots.ToList()
                     from component in slot.Components.ToList()
                     select component)
            {
                if (!(component is ImplementableComponent<IConnector> implementable) || implementable is ParticleSystem) continue;
                count++;
                implementable.Connector.Destroy(false);
                ImplementableComponentPatches.set_Connector(implementable, null);
                //ImplementableComponentPatches.InitializeConnector(implementable);
                implementable.Connector.Initialize();
            }
            UniLog.Log($"Refreshed {count} components");
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static bool Update(FrooxEngineRunner __instance, ref bool ___engineInitialized,
            ref bool ___shutdownEnvironment, ref Engine ___engine, ref Stopwatch ___stopwatch,
            ref Stopwatch ____externalStopwatch, ref SystemInfoConnector ___systemInfoConnector,
            ref Stopwatch ____framerateStopwatch, ref int ____framerateCounter, ref SpinQueue<Action> ___actions,
            ref bool? ____lastVRactive, ref List<World> ____worlds, ref World ____lastFocusedWorld,
            ref bool ___updateDynamicGI, ref bool ___updateDynamicGIimmediate, ref float ___lastDynamicGIupdate,
            ref AudioListener ___audioListener, ref HeadOutput ___vrOutputRoot, ref HeadOutput ___screenOutputRoot)
        {
            if (!___engineInitialized)
                return false;
            UpdateLoop.shutdown = ___shutdownEnvironment;
            if (___shutdownEnvironment)
            {
                UniLog.Log("Shutting down environment");
                try
                {
                    ___engine?.Dispose();
                }
                catch (Exception ex)
                {
                    UniLog.Error("Exception disposing the engine:\n" + ___engine);
                }
                ___engine = null;
                QuitApplication(__instance);
                return false;
            }
            if (!_startedUpdating)
            {
                var updateLoop = new Thread(UpdateLoop.Update)
                {
                    Name = "Update Loop",
                    Priority = ThreadPriority.Normal,
                    IsBackground = false
                };
                updateLoop.Start();
                _startedUpdating = true;
            }
            else
            {
                _lastDiagnosticReport--;
                if (_lastDiagnosticReport <= 0)
                {
                    
                    _lastDiagnosticReport = 3600;
                    _refreshAllConnectors = true;
                    //UniLog.Log("Reinitializing...");
                    //UniLog.Log("SkinnedMeshRenderer: " + UnityEngine.Object.FindObjectsOfType<UnityEngine.SkinnedMeshRenderer>().Length);
                }

                ___stopwatch.Restart();
                ____externalStopwatch.Stop();
                try
                {
                    if (___engine != null)
                    {
                        SystemInfoConnectorPatch.ExternalUpdateTime.SetValue(___systemInfoConnector,
                            ____externalStopwatch.ElapsedMilliseconds * (1f / 1000f));
                        if (!____framerateStopwatch.IsRunning)
                        {
                            ____framerateStopwatch.Restart();
                        }
                        else
                        {
                            ++____framerateCounter;
                            var elapsedMilliseconds = ____framerateStopwatch.ElapsedMilliseconds;
                            if (elapsedMilliseconds >= 500L)
                            {
                                if (___systemInfoConnector != null)
                                    SystemInfoConnectorPatch.FPS.SetValue(___systemInfoConnector, ____framerateCounter /
                                        (elapsedMilliseconds * (1f / 1000f)));
                                ____framerateCounter = 0;
                                ____framerateStopwatch.Restart();
                            }
                        }

                        if (___systemInfoConnector != null)
                        {
                            SystemInfoConnectorPatch.RenderTime.SetValue(___systemInfoConnector,
                                !XRStats.TryGetGPUTimeLastFrame(out var gpuTimeLastFrame) ? -1f
                                    : gpuTimeLastFrame * (1f / 1000f));
                            SystemInfoConnectorPatch.ImmediateFPS.SetValue(___systemInfoConnector,
                                1f / Time.unscaledDeltaTime);
                        }
                        ___engine.InputInterface.UpdateWindowResolution(new int2(Screen.width,
                            Screen.height));
                        //___engine.RunUpdateLoop(6.0);
                        var packets = PacketManager.GetQueuedPackets();
                        foreach (var packet in packets)
                        {
                            try
                            {
                                packet.ApplyChange();
                            }
                            catch (Exception e)
                            {
                                UniLog.Error(e.ToString());
                            }
                        }
                        var assetTaskQueue = PacketManager.GetQueuedAssetTasks();
                        foreach (var task in assetTaskQueue)
                        {
                            try
                            {
                                task();
                            }
                            catch (Exception e)
                            {
                                UniLog.Error(e.ToString());
                            }
                        }
                        var assetIntegrator = Engine.Current.AssetManager.Connector as UnityAssetIntegrator;
                        if (!_renderThreadPointer.HasValue)
                            _renderThreadPointer =
                                (IntPtr) AssetIntegratorPatch.RenderThreadPointer.GetValue(assetIntegrator);
                        /*
                        if (((SpinQueue<>) AssetIntegratorPatch.RenderThreadQueue
                                .GetValue(assetIntegrator))
                            .Count > 0)
                            */
                            GL.IssuePluginEvent(_renderThreadPointer.Value, 0);
                            AssetIntegratorPatch.ProcessQueueMethod.Invoke(assetIntegrator, new object[]
                        {
                            2, false
                        });
                        
                        if (_refreshAllConnectors)
                        {
                            _refreshAllConnectors = false;
                            //RefreshAllConnectors();
                        }

                        var focusedWorld = ___engine.WorldManager.FocusedWorld;
                        if (focusedWorld != null)
                        {
                            var num = ___engine.InputInterface.VR_Active;
                            var headOutput1 = num ? ___vrOutputRoot : ___screenOutputRoot;
                            var headOutput2 = num ? ___screenOutputRoot : ___vrOutputRoot;
                            if (headOutput2 != null &&
                                headOutput2.gameObject.activeSelf)
                                headOutput2.gameObject.SetActive(false);
                            if (!headOutput1.gameObject.activeSelf)
                                headOutput1.gameObject.SetActive(true);
                            headOutput1.UpdatePositioning(focusedWorld);
                            Vector3 vector3;
                            Quaternion quaternion;
                            if (focusedWorld.OverrideEarsPosition)
                            {
                                vector3 = focusedWorld.LocalUserEarsPosition.ToUnity();
                                quaternion = focusedWorld.LocalUserEarsRotation.ToUnity();
                            }
                            else
                            {
                                var cameraRoot = headOutput1.CameraRoot;
                                vector3 = cameraRoot.position;
                                quaternion = cameraRoot.rotation;
                            }

                            var transform = ___audioListener.transform;
                            transform.position = vector3;
                            transform.rotation = quaternion;
                            ___engine.WorldManager.GetWorlds(____worlds);
                            var transform1 = headOutput1.transform;
                            foreach (var world in ____worlds)
                            {
                                if (world.Focus != World.WorldFocus.Overlay &&
                                    world.Focus != World.WorldFocus.PrivateOverlay) continue;
                                var transform2 = ((WorldConnector) world.Connector).WorldRoot.transform;
                                var userGlobalPosition = world.LocalUserGlobalPosition;
                                var userGlobalRotation = world.LocalUserGlobalRotation;
                                transform2.transform.position = transform1.position - userGlobalPosition.ToUnity();
                                transform2.transform.rotation = transform1.rotation * userGlobalRotation.ToUnity();
                                transform2.transform.localScale = transform1.localScale;
                            }

                            ____worlds.Clear();
                        }

                        if (focusedWorld != ____lastFocusedWorld)
                        {
                            FrooxEngineRunner.UpdateDynamicGI(true);
                            ____lastFocusedWorld = focusedWorld;
                            __instance.StartCoroutine(UpdateDynamicGIDelayed(__instance));
                        }

                        var num1 = ___engine.InputInterface.VR_Active ? 1 : 0;
                        var lastVractive = ____lastVRactive;
                        var num2 = lastVractive.GetValueOrDefault() ? 1 : 0;
                        if (!(num1 == num2 & lastVractive.HasValue))
                        {
                            ____lastVRactive = ___engine.InputInterface.VR_Active;
                            if (____lastVRactive.Value)
                            {
                                QualitySettings.lodBias = 3.8f;
                                QualitySettings.vSyncCount = 0;
                                QualitySettings.maxQueuedFrames = 0;
                            }
                            else
                            {
                                QualitySettings.lodBias = 2f;
                                QualitySettings.vSyncCount = 1;
                                QualitySettings.maxQueuedFrames = 2;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UniLog.Error(ex.ToString());
                    Debug.LogError(ex.ToString());
                    ___engine = null;
                    QuitApplication(__instance);
                }

                ____externalStopwatch.Restart();
                ___stopwatch.Stop();
                if (___updateDynamicGI && (___updateDynamicGIimmediate || Time.time - ___lastDynamicGIupdate > 1.0))
                {
                    __instance.GlobalProbe.RenderProbe();
                    DynamicGI.UpdateEnvironment();
                    ___updateDynamicGI = false;
                    ___updateDynamicGIimmediate = false;
                    ___lastDynamicGIupdate = Time.time;
                }

                while (___actions.TryDequeue(out var val1))
                    val1();
                //this would be a memory leak...
                //but it doesn't seem to be used anywhere...
                /*
                while (__instance.messages.TryDequeue(out var val2))
                {
                    if (val2.error)
                        __instance.UnityError(val2.msg);
                    else
                        __instance.UnityLog(val2.msg);
                }
                */
            }
            return false;
        }

        [HarmonyPatch("UpdateDynamicGIDelayed")]
        [HarmonyReversePatch]
        private static IEnumerator UpdateDynamicGIDelayed(FrooxEngineRunner instance) =>
            throw new NotImplementedException();

        [HarmonyPatch("QuitApplication")]
        [HarmonyReversePatch]
        private static void QuitApplication(FrooxEngineRunner instance) =>
            throw new NotImplementedException();
    }

    [HarmonyPatch(typeof(SystemInfoConnector))]
    public static class SystemInfoConnectorPatch
    {
        public static PropertyInfo ExternalUpdateTime =
            typeof(SystemInfoConnector).GetProperty("ExternalUpdateTime", AccessTools.all);
        public static PropertyInfo RenderTime =
            typeof(SystemInfoConnector).GetProperty("RenderTime", AccessTools.all);
        public static PropertyInfo FPS =
            typeof(SystemInfoConnector).GetProperty("FPS", AccessTools.all);
        public static PropertyInfo ImmediateFPS =
            typeof(SystemInfoConnector).GetProperty("ImmediateFPS", AccessTools.all);
        [HarmonyPatch("ExternalUpdateTime", MethodType.Setter)]
        [HarmonyReversePatch]
        public static void set_ExternalUpdateTime(SystemInfoConnector instance, float value) =>
            throw new NotImplementedException();

        [HarmonyPatch("RenderTime", MethodType.Setter)]
        [HarmonyReversePatch]
        public static void set_RenderTime(SystemInfoConnector instance, float value) =>
            throw new NotImplementedException();

        [HarmonyPatch("FPS", MethodType.Setter)]
        [HarmonyReversePatch]
        public static void set_FPS(SystemInfoConnector instance, float value) =>
            throw new NotImplementedException();

        [HarmonyPatch("ImmediateFPS", MethodType.Setter)]
        [HarmonyReversePatch]
        public static void set_ImmediateFPS(SystemInfoConnector instance, float value) =>
            throw new NotImplementedException();
    }
    [HarmonyPatch(typeof(UnityAssetIntegrator))]
    public static class AssetIntegratorPatch
    {
        public static MethodInfo ProcessQueueMethod;
        public static FieldInfo RenderThreadPointer;
        public static FieldInfo RenderThreadQueue;
        static AssetIntegratorPatch()
        {
            ProcessQueueMethod = typeof(UnityAssetIntegrator).GetMethods(AccessTools.all)
                .First(i => i.Name.Contains("ProcessQueue") && i.GetParameters().Length == 2);
            RenderThreadPointer = typeof(UnityAssetIntegrator).GetField("renderThreadPointer", AccessTools.all);
            RenderThreadQueue = typeof(UnityAssetIntegrator).GetField("renderThreadQueue", AccessTools.all);
        }
        /*
        [HarmonyPatch("ProcessQueue", typeof(double), typeof(bool))]
        [HarmonyReversePatch]
        public static int ProcessQueue(UnityAssetIntegrator instance, double maxMilliseconds, bool renderThread) =>
            throw new NotImplementedException("utterly and completely retarded");
            */
    }
}