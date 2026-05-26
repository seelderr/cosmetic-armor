using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CosmeticArmor
{
    public class CosmeticArmorModSystem : ModSystem
    {
        public const string ModId = "cosmeticarmor";
        public const string HideArmorAttribute = ModId + ":hideArmor";

        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;
        private IClientNetworkChannel clientChannel;
        private IServerNetworkChannel serverChannel;
        private GuiDialogCharacterBase characterDialog;
        private Harmony harmony;

        public override void Start(ICoreAPI api)
        {
            harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<ArmorVisibilityTogglePacket>()
                .RegisterMessageType<ArmorVisibilityStatePacket>()
                .SetMessageHandler<ArmorVisibilityStatePacket>(OnServerArmorVisibilityState);

            characterDialog = api.Gui.LoadedGuis.Find(gui => gui is GuiDialogCharacterBase) as GuiDialogCharacterBase;
            if (characterDialog != null)
            {
                characterDialog.ComposeExtraGuis += ComposeArmorGui;
                characterDialog.OnOpened += ComposeArmorGui;
                characterDialog.OnClosed += CloseArmorGui;
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<ArmorVisibilityTogglePacket>()
                .RegisterMessageType<ArmorVisibilityStatePacket>()
                .SetMessageHandler<ArmorVisibilityTogglePacket>(OnClientArmorVisibilityToggle);
        }

        public override void Dispose()
        {
            DisposeArmorGui();
            if (characterDialog != null)
            {
                characterDialog.ComposeExtraGuis -= ComposeArmorGui;
                characterDialog.OnOpened -= ComposeArmorGui;
                characterDialog.OnClosed -= CloseArmorGui;
            }

            harmony?.UnpatchAll(ModId);
        }

        private void ComposeArmorGui()
        {
            if (characterDialog == null) return;

            const double panelWidth = 230;
            const double panelHeight = 128;

            ElementBounds bgBounds = ElementBounds.FixedOffseted(EnumDialogArea.CenterMiddle, 0, 0, 190, 88).WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds dialogBounds = ElementBounds.FixedOffseted(EnumDialogArea.LeftMiddle, 10, -300, panelWidth, panelHeight);

            bool hidden = IsArmorHidden(capi.World.Player.Entity);
            string buttonText = Lang.Get(hidden ? "cosmeticarmor:show-armor" : "cosmeticarmor:hide-armor");

            DisposeArmorGui();
            characterDialog.Composers["cosmeticarmor"] = capi.Gui
                .CreateCompo("cosmeticarmor", dialogBounds)
                .AddShadedDialogBG(bgBounds, true, 5, 0.75f)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("cosmeticarmor:armor-visibility"), CairoFont.WhiteSmallText(), ElementBounds.FixedOffseted(EnumDialogArea.CenterTop, 33, 15, 170, 24))
                    .AddSmallButton(buttonText, OnArmorButtonPressed, ElementBounds.FixedOffseted(EnumDialogArea.CenterMiddle, 0, 20, 160, 26), EnumButtonStyle.Normal, "armorToggle")
                .EndChildElements()
                .Compose(true);
        }

        private bool OnArmorButtonPressed()
        {
            EntityPlayer playerEntity = capi.World.Player.Entity;
            bool hideArmor = !IsArmorHidden(playerEntity);

            ApplyArmorVisibilityState(playerEntity, hideArmor);
            clientChannel.SendPacket(new ArmorVisibilityTogglePacket { HideArmor = hideArmor });
            ComposeArmorGui();

            return true;
        }

        private void CloseArmorGui()
        {
            DisposeArmorGui();
        }

        private void DisposeArmorGui()
        {
            if (characterDialog?.Composers.ContainsKey("cosmeticarmor") != true) return;

            characterDialog.Composers["cosmeticarmor"]?.Dispose();
            characterDialog.Composers.Remove("cosmeticarmor");
        }

        private void OnClientArmorVisibilityToggle(IServerPlayer fromPlayer, ArmorVisibilityTogglePacket packet)
        {
            if (fromPlayer?.Entity == null) return;

            ApplyArmorVisibilityState(fromPlayer.Entity, packet.HideArmor);
            serverChannel.BroadcastPacket(new ArmorVisibilityStatePacket
            {
                EntityId = fromPlayer.Entity.EntityId,
                HideArmor = packet.HideArmor
            });
        }

        private void OnServerArmorVisibilityState(ArmorVisibilityStatePacket packet)
        {
            Entity entity = capi.World.GetEntityById(packet.EntityId);
            if (entity == null) return;

            ApplyArmorVisibilityState(entity, packet.HideArmor);
            if (entity == capi.World.Player.Entity && characterDialog?.IsOpened() == true)
            {
                ComposeArmorGui();
            }
        }

        public static bool IsArmorHidden(Entity entity)
        {
            return entity?.WatchedAttributes?.GetBool(HideArmorAttribute, false) == true;
        }

        public static void ApplyArmorVisibilityState(Entity entity, bool hideArmor)
        {
            if (entity == null) return;

            entity.WatchedAttributes.SetBool(HideArmorAttribute, hideArmor);
            entity.WatchedAttributes.MarkPathDirty(HideArmorAttribute);
            entity.MarkShapeModified();
        }
    }

    [ProtoContract]
    public class ArmorVisibilityTogglePacket
    {
        [ProtoMember(1)]
        public bool HideArmor { get; set; }
    }

    [ProtoContract]
    public class ArmorVisibilityStatePacket
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public bool HideArmor { get; set; }
    }
}
