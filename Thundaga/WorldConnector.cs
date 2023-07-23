using System;
using System.Reflection;
using BaseX;
using FrooxEngine;
using HarmonyLib;
using UnityNeos;

namespace Thundaga
{
    public class WorldConnectorInitializePacket : IConnectorPacket
    {
        private WorldConnector _connector;
        private World _world;
        public void ApplyChange() => WorldConnectorPatch.Initialize(_connector, _world);

        public WorldConnectorInitializePacket(WorldConnector connector, World world)
        {
            _connector = connector;
            _world = world;
        }
    }
    public class WorldConnectorChangeFocusPacket : IConnectorPacket
    {
        private WorldConnector _connector;
        private World.WorldFocus _worldFocus;
        public void ApplyChange() => WorldConnectorPatch.ChangeFocusPatch(_connector, _worldFocus);

        public WorldConnectorChangeFocusPacket(WorldConnector connector, World.WorldFocus worldFocus)
        {
            _connector = connector;
            _worldFocus = worldFocus;
        }
    }
    public class WorldConnectorDestroyPacket : IConnectorPacket
    {
        private WorldConnector _connector;
        public void ApplyChange() => WorldConnectorPatch.DestroyPatch(_connector);

        public WorldConnectorDestroyPacket(WorldConnector connector) => _connector = connector;
    }
    [HarmonyPatch(typeof(WorldConnector))]
    public class WorldConnectorPatch
    {
        public static FieldInfo Focus = typeof(World).GetField("_focus", AccessTools.all);
        [HarmonyPatch("Initialize")]
        [HarmonyReversePatch]
        public static void Initialize(WorldConnector instance, World owner) => throw new NotImplementedException();
        [HarmonyPatch("ChangeFocus")]
        [HarmonyReversePatch]
        public static bool ChangeFocusPatch(WorldConnector __instance, World.WorldFocus focus)
        {
            try
            {
                PacketManager.Enqueue(new WorldConnectorChangeFocusPacket(__instance, focus));
            }
            catch (Exception e)
            {
                UniLog.Error("Error while changing world focus: " + e.ToString());
            }
            return false;
        }
        [HarmonyPatch("Destroy")]
        [HarmonyReversePatch]
        public static bool DestroyPatch(WorldConnector __instance)
        {
            try
            {
                PacketManager.Enqueue(new WorldConnectorDestroyPacket(__instance));
            }
            catch (Exception e)
            {
                UniLog.Error("Error while destroying world connector: " + e.ToString());
            }
            return false;
        }

        [HarmonyPatch("Initialize")]
        [HarmonyPrefix]
        public static bool InitializePatch(WorldConnector __instance, World owner)
        {
            try
            {
                PacketManager.EnqueueHigh(new WorldConnectorInitializePacket(__instance, owner));
            }
            catch (Exception e)
            {
                UniLog.Error("Error while initializing world connector: " + e.ToString());
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(World))]
    public class WorldPatch
    {
        public static int AutoRefreshTick;
        [HarmonyPatch("UpdateUpdateTime")]
        [HarmonyPostfix]
        public static void UpdateUpdateTime(World __instance, double time)
        {
            if (__instance.TotalUpdates == AutoRefreshTick) FrooxEngineRunnerPatch.ShouldRefreshAllConnectors = true;
        }
    }
}