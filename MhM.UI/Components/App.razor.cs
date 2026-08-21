namespace MhM.UI.Components
{
    public partial class App
    {
        private string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }
}
