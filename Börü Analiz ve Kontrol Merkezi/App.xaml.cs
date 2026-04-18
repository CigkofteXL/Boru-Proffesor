using Microsoft.Extensions.DependencyInjection;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new MainPage();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = base.CreateWindow(activationState);

            // Pencerenin en tepesinde görünecek jilet gibi ismi buraya yazıyorsun:
            window.Title = "Börü Analiz ve Komuta Merkezi - VarietyShop";

            return window;
        }
    }
}