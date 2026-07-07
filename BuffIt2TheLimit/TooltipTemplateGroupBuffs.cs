using System;
using BuffIt2TheLimit.Config;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit {
    class TooltipTemplateGroupBuffs : TooltipBaseTemplate {
        private const int MaxEntries = 25;
        private readonly BuffGroup group;

        public TooltipTemplateGroupBuffs(BuffGroup group) {
            this.group = group;
        }

        private string KeyPrefix => group switch {
            BuffGroup.Long => "group.normal",
            BuffGroup.Quick => "group.short",
            BuffGroup.Important => "group.important",
            _ => "group.normal"
        };

        public override IEnumerable<ITooltipBrick> GetHeader(TooltipTemplateType type) {
            yield return new TooltipBrickEntityHeader($"{KeyPrefix}.tooltip.header".i8(), null);
        }

        public override IEnumerable<ITooltipBrick> GetBody(TooltipTemplateType type) {
            List<ITooltipBrick> elements = new();
            elements.Add(new TooltipBrickText($"{KeyPrefix}.tooltip.desc".i8()));
            elements.Add(new TooltipBrickSeparator());

            var state = GlobalBubbleBuffer.Instance?.SpellbookController?.state;
            if (state != null && state.BuffList == null) {
                // BuffList stays null until the first scan; populate the same way
                // the HUD execute path does (BuffExecutor.Execute → Recalculate).
                try {
                    state.Recalculate(false);
                } catch (Exception e) {
                    Main.Error(e, "recalculating buffs for group tooltip");
                }
            }

            var assigned = state?.BuffList?.Where(b => b.InGroups.Contains(group) && b.Requested > 0)
                                           .OrderBy(b => b.Name)
                                           .ToList() ?? new List<BubbleBuff>();

            if (assigned.Count == 0) {
                elements.Add(new TooltipBrickText("group.overview.empty".i8()));
                return elements;
            }

            // The info window (right-click) scrolls, so it gets the full list; hover stays capped.
            var shown = type == TooltipTemplateType.Info ? assigned.Count : MaxEntries;
            foreach (var buff in assigned.Take(shown)) {
                // buff.Icon, not buff.Spell.Icon — null-safe for fused/MagicHack spells
                elements.Add(new TooltipBrickIconAndName(buff.Icon, $"<b>{buff.NameMeta}</b>", TooltipBrickElementType.Small));
            }

            if (assigned.Count > shown) {
                elements.Add(new TooltipBrickText(string.Format("group.overview.more".i8(), assigned.Count - shown)));
            }

            return elements;
        }
    }
}
