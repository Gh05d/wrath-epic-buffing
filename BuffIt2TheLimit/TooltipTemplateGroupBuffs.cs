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

            var buffList = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.BuffList;
            var assigned = buffList?.Where(b => b.InGroups.Contains(group))
                                    .OrderBy(b => b.Name)
                                    .ToList() ?? new List<BubbleBuff>();

            if (assigned.Count == 0) {
                elements.Add(new TooltipBrickText("group.overview.empty".i8()));
                return elements;
            }

            foreach (var buff in assigned.Take(MaxEntries)) {
                // buff.Icon, not buff.Spell.Icon — null-safe for fused/MagicHack spells
                elements.Add(new TooltipBrickIconAndName(buff.Icon, $"<b>{buff.NameMeta}</b>", TooltipBrickElementType.Small));
            }

            if (assigned.Count > MaxEntries) {
                elements.Add(new TooltipBrickText(string.Format("group.overview.more".i8(), assigned.Count - MaxEntries)));
            }

            return elements;
        }
    }
}
