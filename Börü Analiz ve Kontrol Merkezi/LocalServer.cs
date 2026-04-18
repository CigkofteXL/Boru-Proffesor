using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{
    internal class LocalServer
    {
        private HttpListener _listener;
        private bool _isRunning;

        // Eklentiden gelen veriyi arayüze (MainPage) bildirmek için bir olay (Event) tanımlıyoruz
        public event EventHandler<BoruPayload> OnDataReceived;

        public void StartServer()
        {
            try
            {
                _listener = new HttpListener();
                // Eklentinin fetch attığı adres
                _listener.Prefixes.Add("http://localhost:5000/api/xplog/");
                _listener.Start();
                _isRunning = true;

                Debug.WriteLine("🐺 Börü Merkez: Yerel sunucu localhost:5000 üzerinde dinlemeye başladı...");

                // Arayüzü dondurmamak için dinleme işlemini arka plan görevine atıyoruz
                Task.Run(() => ListenAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sunucu başlatılamadı (Port kullanımda olabilir): {ex.Message}");
            }
        }

        private async Task ListenAsync()
        {
            while (_isRunning)
            {
                try
                {
                    // Yeni bir istek gelene kadar burada bekler
                    HttpListenerContext context = await _listener.GetContextAsync();
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;

                    // Tarayıcı eklentisi CORS (Cross-Origin) hatası vermesin diye izinleri veriyoruz
                    response.AppendHeader("Access-Control-Allow-Origin", "*");
                    response.AppendHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
                    response.AppendHeader("Access-Control-Allow-Headers", "Content-Type");

                    // Tarayıcının önden gönderdiği güvenlik (OPTIONS) isteğini direkt onaylayıp geçiyoruz
                    if (request.HttpMethod == "OPTIONS")
                    {
                        response.StatusCode = 200;
                        response.Close();
                        continue;
                    }

                    // Sadece POST isteklerini kabul ediyoruz
                    if (request.HttpMethod == "POST")
                    {
                        // Encoding bazen null gelebilir, o yüzden parametresiz kullanmak en güvenlisidir (UTF-8 varsayar)
                        using StreamReader reader = new StreamReader(request.InputStream);
                        string jsonString = await reader.ReadToEndAsync();

                        // Gelen ham veriyi görmek için logluyoruz
                        System.Diagnostics.Debug.WriteLine($"[HAM JSON GELDİ]: {jsonString}");

                        try
                        {
                            // Büyük/küçük harf duyarlılığını kapatıyoruz (Eklentideki ufak harf hatalarını tolere etsin)
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            BoruPayload gelenVeri = JsonSerializer.Deserialize<BoruPayload>(jsonString, options);

                            if (gelenVeri != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ÇEVİRİ BAŞARILI] Hesap: {gelenVeri.accountnick}");

                                // Veriyi arayüze (MainPage) gönderiyoruz
                                OnDataReceived?.Invoke(this, gelenVeri);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Eğer JSON çevrilirken patlarsa, bize nedenini söylesin
                            System.Diagnostics.Debug.WriteLine($"[JSON ÇEVİRİ HATASI]: {ex.Message}");
                        }
                    }

                    // Eklentiye "Veriyi aldım, sıkıntı yok" (200 OK) cevabı dönüyoruz
                    response.StatusCode = 200;
                    response.Close();
                }
                catch (HttpListenerException)
                {
                    // Sunucu durdurulduğunda fırlatılan normal bir hata, yoksayabiliriz
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"İstek işlenirken hata oluştu: {ex.Message}");
                }
            }
        }

        public void StopServer()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            Debug.WriteLine("🐺 Börü Merkez: Sunucu durduruldu.");
        }
    }
}
