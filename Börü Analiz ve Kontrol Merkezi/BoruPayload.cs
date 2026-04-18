using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{
    public class BoruPayload
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string accountnick { get; set; }
        public string accountid { get; set; }
        public long timestamp { get; set; }
        public int xpGained { get; set; }
        public int currentLevel { get; set; }
        public int totalgold { get; set; }
        public string playedRole { get; set; }
        public string matchResult { get; set; }
        public int matchDurationSec { get; set; }
        public bool isMobile { get; set; }

        // --- XAML İÇİN GÖRSEL BAĞLAMALAR ---

        [Ignore]
        public string PlatformIcon => isMobile ? "📱 Mobil" : "💻 Masaüstü";

        [Ignore]
        public string KazanimOzet => $"+{xpGained} XP | {totalgold} 🪙";

        [Ignore]
        public string GorselSure => matchDurationSec > 1000000 ? "Hatalı Süre" : $"{matchDurationSec} sn";

        [Ignore]
        public string GorselRol => string.IsNullOrWhiteSpace(playedRole) ? "Bilinmiyor" : playedRole;

        // NİCKLERİ BURASI GETİRİYOR - EĞER BOŞSA BURADANDIR
        [Ignore]
        public string MaskeliNick
        {
            get
            {
                // MainPage'deki static gizlilik değişkenine bakıyoruz
                if (!MainPage.IsGizlilikModuAcik || string.IsNullOrEmpty(accountnick))
                    return accountnick;

                if (accountnick.Length <= 2) return "**";
                return accountnick.Substring(0, 2) + new string('*', accountnick.Length - 2);
            }
        }

        [Ignore]
        public string MezuniyetDurumu
        {
            get
            {
                // Hedef ve Mevcut Level'ı karşılaştırırken hata payını sıfırlıyoruz
                long hedef = (long)MainPage.GlobalHedefLevel;

                if (currentLevel >= hedef) return "✅ TAMAMLANDI";

                // Bot hızı (XPM)
                double botXpm = (matchDurationSec > 0 && matchDurationSec < 10000)
                                ? (xpGained / (matchDurationSec / 60.0))
                                : 50;

                if (botXpm <= 0) botXpm = 1;

                // Kalan XP ve Süre
                double kalanXp = (double)(hedef - currentLevel) * 2000;
                double kalanDakika = kalanXp / botXpm;

                if (kalanDakika > 1440) return $"⌛ {(int)(kalanDakika / 1440)} gün+";
                if (kalanDakika > 60) return $"⌛ {(int)(kalanDakika / 60)}s {(int)(kalanDakika % 60)}dk";
                return $"⌛ {(int)kalanDakika} dk kaldı";
            }
        }
    }
}
