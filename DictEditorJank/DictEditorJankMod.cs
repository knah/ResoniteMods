using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;

namespace DictEditorJank;

public class DictEditorJankMod : CommonModBase<DictEditorJankMod>
{
    public override string Name => "DictEditorJank";
    
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> Enabled = new("Enabled", "Generate editors for dictionaries?", () => true);

    public override void OnEngineInit()
    {
        HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
    }
    
	[HarmonyPatch(typeof(SyncMemberEditorBuilder), "Build")]
	internal sealed class DictEditor {
		public static void Postfix(ISyncMember member,
			string name,
			FieldInfo fieldInfo,
			UIBuilder ui,
			float labelSize) {
			if (!CommonSettings.GetValue(Enabled)) {
				return;
			}

			if (member is SyncVar var) {
				// Technically ignores changes if the element changes type
				SyncMemberEditorBuilder.Build(var.Element, name, fieldInfo, ui, labelSize);
				return;
			}
			
			if (member is not ISyncDictionary dictionary)
				return;

			ui.PushStyle();
			ui.Style.MinHeight = -1f;
			ui.VerticalLayout(4f);
			ui.Style.MinHeight = 24f;
			Text text = ui.Text((LocaleString) (name + " (dict):"), parseRTF: false);
			colorX b = text.GetType().GetTypeColor().MulRGB(1.5f);
			InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>().ColorDrivers.Add();
			colorDriver.ColorDrive.Target = text.Color;
			colorDriver.NormalColor.Value = MathX.LerpUnclamped(RadiantUI_Constants.TEXT_COLOR, in b, 0.1f);
			colorDriver.HighlightColor.Value = RadiantUI_Constants.LABEL_COLOR;
			colorDriver.PressColor.Value = RadiantUI_Constants.HEADING_COLOR;
			text.Slot.AttachComponent<ReferenceProxySource>().Reference.Target = dictionary;
			ui.Style.MinHeight = -1f;
			ui.VerticalLayout(4f);
			
			// comps on ui.Root
			var slotToFill = ui.Root;
			
			ui.NestOut();
			ui.Style.MinHeight = 24f;
			ui.NestOut();

			var lastBuildData = new Dictionary<string, (Slot, ISyncMember)>();
			var slotsToRemove = new HashSet<string>();
			void BuildDict()
			{
				foreach (var key in lastBuildData.Keys) 
					slotsToRemove.Add(key);

				foreach (var (k, v) in dictionary.BoxedEntries)
				{
					var keyString = k.ToString() ?? "<null>";
					slotsToRemove.Remove(keyString);
					if (lastBuildData.TryGetValue(keyString, out var last))
					{
						if (ReferenceEquals(v, last.Item2))
							continue;
						
						last.Item1.Destroy();
						lastBuildData.Remove(keyString);
					}
					
					var childSlot = slotToFill.AddSlot("Element");
					lastBuildData.Add(keyString, (childSlot, v));
					BuildDictItem(keyString, v, childSlot, fieldInfo, labelSize);
				}
				
				foreach (var sn in slotsToRemove)
					if (lastBuildData.TryGetValue(sn, out var last))
					{
						last.Item1.Destroy();
						lastBuildData.Remove(sn);
					}
				
				slotsToRemove.Clear();
			}
			
			BuildDict();

			Action<IChangeable> dictionaryOnChanged = _ => {
				BuildDict();
			};
			dictionary.Changed += dictionaryOnChanged;
			slotToFill.FindNearestParent<IDestroyable>().Destroyed += _ => dictionary.Changed -= dictionaryOnChanged;
			
			ui.PopStyle();
		}
		
		public static void BuildDictItem(string keyName, ISyncMember listItem, Slot root, FieldInfo fieldInfo, float labelSize)
		{
			root.AttachComponent<HorizontalLayout>().Spacing.Value = 4f;
			UIBuilder ui = new UIBuilder(root);
			RadiantUI_Constants.SetupEditorStyle(ui);
			ui.Style.RequireLockInToPress = true;
			ui.Style.MinWidth = -1f;
			ui.Style.FlexibleWidth = 100f;
			SyncMemberEditorBuilder.Build(listItem, keyName, fieldInfo, ui, labelSize);
			ui.Style.FlexibleWidth = 0.0f;
			ui.Style.MinWidth = 24f;
		}
	}

}