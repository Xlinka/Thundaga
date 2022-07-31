using System;
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
        public void ApplyChange() => WorldConnectorPatch.ChangeFocus(_connector, _worldFocus);

        public WorldConnectorChangeFocusPacket(WorldConnector connector, World.WorldFocus worldFocus)
        {
            _connector = connector;
            _worldFocus = worldFocus;
        }
    }
    public class WorldConnectorDestroyPacket : IConnectorPacket
    {
        private WorldConnector _connector;
        public void ApplyChange() => WorldConnectorPatch.Destroy(_connector);

        public WorldConnectorDestroyPacket(WorldConnector connector) => _connector = connector;
    }
    [HarmonyPatch(typeof(WorldConnector))]
    public class WorldConnectorPatch
    {
        [HarmonyPatch("Initialize")]
        [HarmonyReversePatch]
        public static void Initialize(WorldConnector instance, World owner) => throw new NotImplementedException();
        [HarmonyPatch("ChangeFocus")]
        [HarmonyReversePatch]
        public static void ChangeFocus(WorldConnector instance, World.WorldFocus focus) => throw new NotImplementedException();
        [HarmonyPatch("Destroy")]
        [HarmonyReversePatch]
        public static void Destroy(WorldConnector instance) => throw new NotImplementedException();

        [HarmonyPatch("Initialize")]
        [HarmonyPrefix]
        public static bool InitializePatch(WorldConnector __instance, World owner)
        {
            PacketManager.EnqueueHigh(new WorldConnectorInitializePacket(__instance, owner));
            return false;
        }
        [HarmonyPatch("ChangeFocus")]
        [HarmonyPrefix]
        public static bool ChangeFocusPatch(WorldConnector __instance, World.WorldFocus focus)
        {
            PacketManager.Enqueue(new WorldConnectorChangeFocusPacket(__instance, focus));
            return false;
        }
        [HarmonyPatch("Destroy")]
        [HarmonyPrefix]
        public static bool DestroyPatch(WorldConnector __instance)
        {
            PacketManager.Enqueue(new WorldConnectorDestroyPacket(__instance));
            return false;
        }
    }
}