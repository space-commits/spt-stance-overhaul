using System.Collections.Generic;
using EFT.InventoryLogic;
using RealismCommonLib.ItemStats;
using RealismCommonLib.Schema;

namespace StanceOverhaul.ItemStats
{
    // Inspect panel rows for the face shield ADS rules (Item-Stats-ADS-Plan.md §4.5). Only added while the face shield
    // aim block is on when the item is built, since they describe what that feature does.
    // Text is in src/Resources/Locales/<lang>.json; the face shield, gear and stock tooltips are legacy's item
    // description lines (description_gen.ts).
    public static class FaceShieldAdsAttributes
    {
        private static readonly string BlocksAdsKey = ItemAttributeRegistry.LocaleKey(ItemAttributeIds.RealismBlocksAds);
        private static readonly string FaceShieldAdsKey = ItemAttributeRegistry.LocaleKey(ItemAttributeIds.RealismFaceShieldAds);
        private static readonly string AllowsAdsTip = ItemAttributeRegistry.LocaleKey(ItemAttributeIds.RealismAllowsAds) + "/Tooltip";
        private static readonly string StockAllowsTip = ItemAttributeRegistry.LocaleKey(ItemAttributeIds.RealismAllowsFaceShieldAds) + "/Tooltip";
        private static readonly string ShoulderContactTip = ItemAttributeRegistry.LocaleKey(ItemAttributeIds.RealismShoulderContact) + "/Tooltip";
        private const string VanillaYes = "Yes"; // vanilla key, already translated

        public static void Register()
        {
            RealismLocales.AddEmbedded(typeof(FaceShieldAdsAttributes).Assembly);
            ItemAttributeRegistry.Register(CreateRows);

            // Legacy's CanADS/CantADS rows used the SightingRange icon (Item-Stats-Icons-Plan.md §7).
            IconSource sightingRange = IconSource.Vanilla(EItemAttributeId.SightingRange);
            ItemAttributeRegistry.SetIcon(ItemAttributeIds.RealismBlocksAds, sightingRange);
            ItemAttributeRegistry.SetIcon(ItemAttributeIds.RealismAllowsAds, sightingRange);
            ItemAttributeRegistry.SetIcon(ItemAttributeIds.RealismAllowsFaceShieldAds, sightingRange);
            ItemAttributeRegistry.SetIcon(ItemAttributeIds.RealismFaceShieldAds, sightingRange);
            ItemAttributeRegistry.SetIcon(ItemAttributeIds.RealismShoulderContact, IconSource.Vanilla(EItemAttributeId.Ergonomics));
        }

        private static IEnumerable<ItemAttribute>? CreateRows(Item item)
        {
            if (!PluginConfig.EnableFSAimBlock.Value) return null;

            // Runs in the base Item constructor: the item's own components (e.g. FaceShieldComponent) don't exist yet,
            // so anything that needs them is read inside the value funcs.
            List<ItemAttribute>? rows = null;

            if (item is Weapon weapon)
            {
                return new[]
                {
                    ItemAttributeRegistry.CreateLocalizedCompact(ItemAttributeIds.RealismFaceShieldAds,
                        () => $"{FaceShieldAdsKey}/Value/{WeaponRules.Evaluate(weapon).FaceShieldAds}",
                        () => $"{FaceShieldAdsKey}/Tooltip/{WeaponRules.Evaluate(weapon).FaceShieldAds}"),
                };
            }

            if (item is Mod mod)
            {
                StockKind kind = WeaponRules.GetStockKind(mod);
                if (!kind.IsShoulderStock()) return null;

                rows = new List<ItemAttribute>();
                if (mod.Template.HasShoulderContact)
                    rows.Add(ItemAttributeRegistry.CreateLocalizedCompact(ItemAttributeIds.RealismShoulderContact, () => VanillaYes, () => ShoulderContactTip));
                if (mod.Template.RealismStockAllowADS)
                    rows.Add(ItemAttributeRegistry.CreateLocalizedCompact(ItemAttributeIds.RealismAllowsFaceShieldAds, () => "", () => StockAllowsTip));
                return rows;
            }

            // Face shields (template flag, so welding masks and fixed visors count too) that don't block, the counterpart
            // to "Blocks ADS". Other gear allows ADS by default, so it gets no row.
            if (item.Template.RealismAllowADS)
            {
                if (item.Template is ArmoredEquipmentTemplate armored && armored.FaceShieldComponent)
                    return new[] { ItemAttributeRegistry.CreateLocalizedCompact(ItemAttributeIds.RealismAllowsAds, () => "", () => AllowsAdsTip) };
                return null;
            }

            return new[]
            {
                ItemAttributeRegistry.CreateLocalizedCompact(ItemAttributeIds.RealismBlocksAds,
                    () => $"{BlocksAdsKey}/Value/{BlockCase(item)}",
                    () => $"{BlocksAdsKey}/Tooltip/{BlockCase(item)}"),
            };
        }

        private static string BlockCase(Item item) => item.GetItemComponent<FaceShieldComponent>()?.Togglable != null ? "WhenLowered" : "Always";
    }
}
