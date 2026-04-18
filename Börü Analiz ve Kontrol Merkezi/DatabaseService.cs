using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{
    internal class DatabaseService
    {
        private SQLiteAsyncConnection _db;

        public DatabaseService()
        {
            Init();
        }

        private async void Init()
        {
            if (_db != null) return;

            // Veritabanı dosyasının kaydedileceği güvenli konum (PC'de AppData, Mobilde özel alan)
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "BoruDB.db3");

            _db = new SQLiteAsyncConnection(dbPath);

            // Tabloyu oluşturur (Eğer daha önce oluşturulduysa dokunmaz)
            await _db.CreateTableAsync<BoruPayload>();
        }

        // Eklentiden gelen veriyi veritabanına zımbalayan metot
        public async Task YeniVeriKaydetAsync(BoruPayload yeniVeri)
        {
            await _db.InsertAsync(yeniVeri);
        }

        // İleride arayüzdeki tabloyu doldurmak için kullanacağımız metot (Sondan başa doğru sıralı)
        public async Task<List<BoruPayload>> SonVerileriGetirAsync()
        {
            return await _db.Table<BoruPayload>().OrderByDescending(x => x.timestamp).ToListAsync();
        }


        public async Task TumVerileriSilAsync()
        {
             Init(); // Bağlantı yoksa kur
            await _db.DeleteAllAsync<BoruPayload>(); // Tablodaki tüm verileri nükleer atışla siler
        }
        public async Task EskiVerileriTemizleAsync(int gunSayisi)
        {
            if (gunSayisi <= 0) return;

            // Milisaniye cinsinden o günden öncesini bul
            long limitZaman = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (gunSayisi * 24L * 3600 * 1000);

             Init();
            // SQL komutuyla o tarihten eski her şeyi tek hamlede siliyoruz
            await _db.ExecuteAsync("DELETE FROM BoruPayload WHERE timestamp < ?", limitZaman);
        }
        // Sadece seçilen ID'ye sahip satırı kalıcı olarak siler
        public async Task VeriSilAsync(int id)
        {
             Init();
            await _db.DeleteAsync<BoruPayload>(id);
        }
    }
}
