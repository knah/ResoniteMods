using HarmonyLib;
using ResoniteModLoader;

public abstract class CommonModBase<T> : ResoniteMod where T: CommonModBase<T>
{
    public override string Author => "knah";
    public override string Version => typeof(T).Assembly.GetName().Version.ToString();
    public override string Link => "https://github.com/knah/ResoniteMods";

    protected static T CommonInstance;
    protected readonly Harmony HarmonyInstance = new($"me.knah.{typeof(T).Name}");

    public CommonModBase()
    {
        CommonInstance = (T) this;
    }

    internal static ModConfiguration CommonSettings => CommonInstance.GetConfiguration()!;
}