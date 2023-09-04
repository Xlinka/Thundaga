using System;
using System.Reflection;
using FrooxEngine;
using HarmonyLib;
using UnityNeos;
using UnityEngine;
using BaseX;
namespace Thundaga
{
    public class WorldConnectorInitializePacket : IConnectorPacket
    {
        private WorldConnector _connector;
        private World _world;
        public void ApplyChange()
        {
            WorldConnectorPatch.Initialize(_connector, _world);
            Unilog.Log("WorldConnectorInitializePacket Applied.");
        }

        public WorldConnectorInitializePacket(WorldConnector connector, World world)
        {
            _connector = connector;
            _world = world;
            Unilog.Log($"Created WorldConnectorInitializePacket for World: {_world}");
        }
    }

    public class WorldConnectorChangeFocusPacket : IConnectorPacket
    {
        private WorldConnector _connector;
        private World.WorldFocus _worldFocus;
        public void ApplyChange()
        {
            WorldConnectorPatch.ChangeFocus(_connector, _worldFocus);
            Unilog.Log("WorldConnectorChangeFocusPacket Applied.");
        }

        public WorldConnectorChangeFocusPacket(WorldConnector connector, World.WorldFocus worldFocus)
        {
            _connector = connector;
            _worldFocus = worldFocus;
            Unilog.Log($"Created WorldConnectorChangeFocusPacket for World: {_worldFocus}");
        }
    }

    public class WorldConnectorDestroyPacket : IConnectorPacket
    {
        private WorldConnector _connector;
        public void ApplyChange()
        {
            WorldConnectorPatch.Destroy(_connector);
            Unilog.Log("WorldConnectorDestroyPacket Applied.");
        }

        public WorldConnectorDestroyPacket(WorldConnector connector)
        {
            _connector = connector;
            Unilog.Log($"Created WorldConnectorDestroyPacket for WorldConnector: {_connector}");
        }
    }

    [HarmonyPatch(typeof(WorldConnector))]
    public class WorldConnectorPatch
    {
        public static FieldInfo Focus = typeof(World).GetField("_focus", AccessTools.all);

        [HarmonyPatch("Initialize")]
        [HarmonyReversePatch]
        public static void Initialize(WorldConnector instance, World owner)
        {
            Unilog.Log("WorldConnector Initialize Called.");
            throw new NotImplementedException();
        }

        [HarmonyPatch("ChangeFocus")]
        [HarmonyReversePatch]
        public static void ChangeFocus(WorldConnector instance, World.WorldFocus focus)
        {
            Unilog.Log($"WorldConnector ChangeFocus Called. New Focus: {focus}");
            throw new NotImplementedException();
        }

        [HarmonyPatch("Destroy")]
        [HarmonyReversePatch]
        public static void Destroy(WorldConnector instance)
        {
            Unilog.Log("WorldConnector Destroy Called.");
            throw new NotImplementedException();
        }

        [HarmonyPatch("Initialize")]
        [HarmonyPrefix]
        public static bool InitializePatch(WorldConnector __instance, World owner)
        {
            PacketManager.EnqueueHigh(new WorldConnectorInitializePacket(__instance, owner));
            Unilog.Log("WorldConnector InitializePatch Called.");
            return false;
        }

        [HarmonyPatch("ChangeFocus")]
        [HarmonyPrefix]
        public static bool ChangeFocusPatch(WorldConnector __instance, World.WorldFocus focus)
        {
            PacketManager.Enqueue(new WorldConnectorChangeFocusPacket(__instance, focus));
            Unilog.Log("WorldConnector ChangeFocusPatch Called.");
            return false;
        }

        [HarmonyPatch("Destroy")]
        [HarmonyPrefix]
        public static bool DestroyPatch(WorldConnector __instance)
        {
            PacketManager.Enqueue(new WorldConnectorDestroyPacket(__instance));
            Unilog.Log("WorldConnector DestroyPatch Called.");
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
            if (__instance.TotalUpdates == AutoRefreshTick)
            {
                FrooxEngineRunnerPatch.ShouldRefreshAllConnectors = true;
                Unilog.Log("AutoRefreshTick reached. ShouldRefreshAllConnectors set to true.");
            }
        }
    }
}
