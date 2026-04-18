using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{
    internal class PlatformConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMobile)
            {
                return isMobile ? "📱 Mobil" : "💻 Masaüstü";
            }
            return "Bilinmiyor";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
