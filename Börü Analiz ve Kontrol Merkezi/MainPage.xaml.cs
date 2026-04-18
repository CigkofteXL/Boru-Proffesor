using Fleck;
using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Börü_Analiz_ve_Kontrol_Merkezi
{


    public partial class MainPage : ContentPage
    {
        // Artık sadece soketleri değil, botların tüm kimliğini tutuyoruz!
        private Dictionary<Guid, AktifBotVerisi> _canliTim = new Dictionary<Guid, AktifBotVerisi>();
        // Botun Kimlik Kartı
        public class AktifBotVerisi
        {
            public IWebSocketConnection Soket { get; set; }
            public string Nick { get; set; } = "Bilinmeyen Asker";
            public string Durum { get; set; } = "Bağlanıyor...";
            public string Rol { get; set; } = "?";
            public int Altin { get; set; } = 0;
            public int Level { get; set; } = 0;
            public bool SeciliMi { get; set; } = true; // Varsayılan olarak göreve hazır (Seçili) gelsin
        }
        // JS'den gelen ayarları C#'a çeviren harita
        public class JsAyarlar
        {
            public bool AUTO_PLAY { get; set; }
            public bool AUTO_REPLAY { get; set; }
            public bool AUTO_JOIN_ROOMS { get; set; }
            public bool AUTO_CREATE_ROOM { get; set; }
            public bool LOBBY_AUTO_QUIT_ACTIVE { get; set; }
            public bool AUTO_JOIN_CASE_SENSITIVE { get; set; }
            public bool DEBUG_MODE { get; set; }
            public bool SHOW_HIDDEN_LVL { get; set; }
            public bool TELEMETRY_ACTIVE { get; set; }
            public bool CHAT_STATS { get; set; }
            public bool CHAT_SOUND { get; set; }

            public string AUTO_JOIN_FILTER { get; set; }
            public string AUTO_JOIN_EXCLUDE { get; set; }
            public string AUTO_JOIN_PASSWORD { get; set; }
            public string AUTO_CREATE_TEMPLATE_NAME { get; set; }
            public string USER_P2P_CODE { get; set; }

            public int AUTO_SLOT { get; set; }
            public int LOBBY_AUTO_QUIT_SECONDS { get; set; }
            public int AUTO_REFRESH_INTERVAL { get; set; }
            public int WAITING_HOST_TIMEOUT { get; set; }
        }

        // JS'den gelen JSON'u okumak için kalıp
        public class GelenTelsizMesaji
        {
            public string tip { get; set; }
            public string komut { get; set; }
            public string nick { get; set; }
            public string id { get; set; }
            public string durum { get; set; }
            public string rol { get; set; }
            public int altin { get; set; }
            public int level { get; set; }
            public JsAyarlar ayarlar { get; set; } // 🔥 YENİ EKLENDİ
        }

        private LocalServer _apiServer;
        private DatabaseService _dbService;
        private bool _gizlilikAktif = false;
        // Her yerden erişilebilir hedef level (Varsayılan 50)
        public static int GlobalHedefLevel = 50;
        public static bool IsGizlilikModuAcik = false; // Model buraya bakıp nick gizleyecek
        private const string ConfigBakimAyar = "BakimAyarIndex";
        public ObservableCollection<BoruPayload> GelenVeriler { get; set; } = new ObservableCollection<BoruPayload>();


        public MainPage()
        {
            InitializeComponent();
            // Kuleyi İnşa Et ve Dinlemeye Başla
            ButonlariAteslemeyeBagla();
            TelsizKulesiniDik();

            _dbService = new DatabaseService();
            _apiServer = new LocalServer();

            BotVerileriListesi.ItemsSource = GelenVeriler;

            GecmisVerileriYukle();

            _apiServer.OnDataReceived += async (sender, payload) =>
            {
                try
                {
                    // SQLite'a kaydetmeyi dene
                    await _dbService.YeniVeriKaydetAsync(payload);

                    // Arayüzü güncelle
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Yeni veriyi tabloya ekle
                        GelenVeriler.Insert(0, payload);

                        // Filtre menülerini anında güncelle
                        FiltreyeDinamikEkle(PickerHesap, payload.accountnick);
                        FiltreyeDinamikEkle(PickerSonuc, payload.matchResult);

                        // Rol boş gelirse "Bilinmiyor" olarak filtreye ekle
                        string rol = string.IsNullOrWhiteSpace(payload.playedRole) ? "Bilinmiyor" : payload.playedRole;
                        FiltreyeDinamikEkle(PickerRol, rol);
                    });

                    // 🔥 KONTAK ÇEVRİLDİ 2: Yeni maç bittiğinde tepe kartları anında güncellensin!
                    await KpiKartlariniGuncelleAsync();
                    await SaatlikIsiHaritasiniGuncelleAsync(); // <--- BUNU EKLE
                    await GrafikleriGuncelleAsync(); // <--- BU BURADA OLMALI
                    AnalizleriHesapla();
                }
                catch (Exception ex)
                {
                    // Eğer SQLite patlarsa bize nedenini bağıra bağıra söylesin!
                    System.Diagnostics.Debug.WriteLine($"❌ SQLITE veya ARAYÜZ HATASI: {ex.Message}");
                }
            };

            _apiServer.StartServer();
        }


        // 📡 BÖRÜ TELSİZ KULESİ (WebSocket Sunucusu)
        private WebSocketServer _kule;
        private List<IWebSocketConnection> _aktifTim = new List<IWebSocketConnection>();

        // 🔥 AYAR ŞABLONLARI İÇİN HAFIZA VE MODEL 🔥
        private Dictionary<string, AyarSablonu> _kayitliSablonlar = new Dictionary<string, AyarSablonu>();

        public class AyarSablonu
        {
            // Şalterler
            public bool AutoPlay { get; set; }
            public bool AutoReplay { get; set; }
            public bool AutoJoin { get; set; }
            public bool AutoCreate { get; set; }
            public bool LobbyQuit { get; set; }
            public bool JoinCase { get; set; }
            public bool Debug { get; set; }
            public bool ShowHidden { get; set; }
            public bool Telemetry { get; set; }
            public bool ChatStats { get; set; }
            public bool ChatSound { get; set; }

            // Metin ve Sayılar
            public string JoinFilter { get; set; }
            public string JoinExclude { get; set; }
            public string JoinPassword { get; set; }
            public string CreateTemplate { get; set; }
            public string Slot { get; set; }
            public string P2PCode { get; set; }
            public string LobbyQuitSec { get; set; }
            public string RefreshMin { get; set; }
            public string HostTimeout { get; set; }
        }

        // 🔥 BÖRÜSSENGER P2P KİMLİK MODELLERİ 🔥
        public class P2PKisi { public string id { get; set; } public string name { get; set; } public string avatar { get; set; } }
        public class P2PMesaj { public string id { get; set; } public string sender { get; set; } public string msg { get; set; } public string type { get; set; } }

        public class GelenBorussengerSync
        {
            public string tip { get; set; }
            public List<P2PKisi> friends { get; set; }
            public Dictionary<string, List<P2PMesaj>> chats { get; set; }
        }

        public class GelenBorussengerCanli
        {
            public string tip { get; set; }
            public string peerID { get; set; }
            public P2PMesaj mesaj { get; set; }
        }

        // C#'ın Hafızası (Anlık olarak kiminle konuştuğumuzu ve tüm sohbetleri tutacak)
        private string _aktifBorussengerHedef = null;
        private string _aktifBorussengerHedefIsim = null; // 🔥 YENİ EKLENEN 🔥
        private Dictionary<string, List<P2PMesaj>> _csharpSohbetHafizasi = new Dictionary<string, List<P2PMesaj>>();

        // ==========================================
        // 1. TELSİZ KULESİ KURULUMU (FLECK)
        // ==========================================
        private void TelsizKulesiniDik()
        {
            _kule = new WebSocketServer("ws://127.0.0.1:8181");
            _kule.Start(soket =>
            {
                soket.OnOpen = () =>
                {
                    // Asker bağlandı ama henüz kim olduğunu bilmiyoruz. Hafızaya al.
                    _canliTim.Add(soket.ConnectionInfo.Id, new AktifBotVerisi { Soket = soket });
                    TimListesiniArayuzeCiz();
                };

                soket.OnClose = () =>
                {
                    // Askerin bağlantısı koptu, listeden sil ve arayüzü güncelle
                    if (_canliTim.ContainsKey(soket.ConnectionInfo.Id))
                    {
                        _canliTim.Remove(soket.ConnectionInfo.Id);
                        TimListesiniArayuzeCiz();
                    }
                };

                soket.OnMessage = mesaj =>
                {
                    try
                    {
                        // Ping-Pong Kontrolü (JS tarafındaki "1" - "2" olayı)
                        if (mesaj == "2") return;

                        // Gelen JSON'u çöz
                        var gelenVeri = JsonSerializer.Deserialize<GelenTelsizMesaji>(mesaj);

                        if (gelenVeri != null && gelenVeri.tip == "CANLI_RAPOR")
                        {
                            // Hangi askerden geldiğini bul ve kimliğini güncelle
                            if (_canliTim.TryGetValue(soket.ConnectionInfo.Id, out var asker))
                            {
                                asker.Nick = gelenVeri.nick;
                                asker.Durum = gelenVeri.durum;
                                asker.Rol = gelenVeri.rol;
                                asker.Altin = gelenVeri.altin;
                                asker.Level = gelenVeri.level;

                                // Kimlik güncellendi, arayüzü (sol paneli) baştan çiz!
                                TimListesiniArayuzeCiz();
                            }
                        }
                        if (gelenVeri != null && (gelenVeri.tip == "AYAR_SYNC" || gelenVeri.tip == "CANLI_RAPOR"))
                        {
                            if (gelenVeri.ayarlar != null)
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    // Şalterleri (Switch) JS'ye göre ayarla
                                    SwAutoPlay.IsToggled = gelenVeri.ayarlar.AUTO_PLAY;
                                    SwAutoReplay.IsToggled = gelenVeri.ayarlar.AUTO_REPLAY;
                                    SwAutoJoin.IsToggled = gelenVeri.ayarlar.AUTO_JOIN_ROOMS;
                                    SwAutoCreate.IsToggled = gelenVeri.ayarlar.AUTO_CREATE_ROOM;
                                    SwLobbyQuit.IsToggled = gelenVeri.ayarlar.LOBBY_AUTO_QUIT_ACTIVE;
                                    SwJoinCase.IsToggled = gelenVeri.ayarlar.AUTO_JOIN_CASE_SENSITIVE;
                                    SwDebug.IsToggled = gelenVeri.ayarlar.DEBUG_MODE;
                                    SwShowHidden.IsToggled = gelenVeri.ayarlar.SHOW_HIDDEN_LVL;
                                    SwTelemetry.IsToggled = gelenVeri.ayarlar.TELEMETRY_ACTIVE;
                                    SwChatStats.IsToggled = gelenVeri.ayarlar.CHAT_STATS;
                                    SwChatSound.IsToggled = gelenVeri.ayarlar.CHAT_SOUND;

                                    // Kutuları (Entry) JS'ye göre doldur
                                    EntJoinFilter.Text = gelenVeri.ayarlar.AUTO_JOIN_FILTER;
                                    EntJoinExclude.Text = gelenVeri.ayarlar.AUTO_JOIN_EXCLUDE;
                                    EntJoinPassword.Text = gelenVeri.ayarlar.AUTO_JOIN_PASSWORD;
                                    EntCreateTemplate.Text = gelenVeri.ayarlar.AUTO_CREATE_TEMPLATE_NAME;
                                    EntP2PCode.Text = gelenVeri.ayarlar.USER_P2P_CODE;

                                    EntSlot.Text = gelenVeri.ayarlar.AUTO_SLOT.ToString();
                                    EntLobbyQuit.Text = gelenVeri.ayarlar.LOBBY_AUTO_QUIT_SECONDS.ToString();
                                    EntRefresh.Text = gelenVeri.ayarlar.AUTO_REFRESH_INTERVAL.ToString();
                                    EntHostTimeout.Text = gelenVeri.ayarlar.WAITING_HOST_TIMEOUT.ToString();
                                });
                                System.Diagnostics.Debug.WriteLine("⚙️ [Börü] C# Arayüzü botun gerçek ayarlarına göre senkronize edildi.");
                            }
                        }
                        // ==========================================
                        // 🐺 BÖRÜSSENGER: FULL SENKRONİZASYON GELDİ
                        // ==========================================
                        else if (mesaj.Contains("\"tip\":\"BORUSSENGER_FULL_SYNC\""))
                        {
                            var syncData = JsonSerializer.Deserialize<GelenBorussengerSync>(mesaj);
                            if (syncData != null)
                            {
                                // JS'den gelen devasa chat dosyasını C# hafızasına kopyala
                                _csharpSohbetHafizasi = syncData.chats ?? new Dictionary<string, List<P2PMesaj>>();

                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    SlKisiler.Children.Clear();

                                    // Rehberde (JS localstorage) kişi var mı?
                                    if (syncData.friends != null && syncData.friends.Count > 0)
                                    {
                                        foreach (var kisi in syncData.friends)
                                        {
                                            // Kişileri sol listeye canlı olarak ekle!
                                            GercekKisiEkle(kisi.id, kisi.name, "#4CAF50");
                                        }
                                    }
                                    else
                                    {
                                        SlKisiler.Children.Add(new Label { Text = "Rehber boş...", TextColor = Colors.Gray, Margin = new Thickness(15) });
                                    }
                                });
                            }
                        }

                        // ==========================================
                        // 🐺 BÖRÜSSENGER: CANLI YENİ MESAJ GELDİ
                        // ==========================================
                        else if (mesaj.Contains("\"tip\":\"BORUSSENGER_CANLI_MESAJ\""))
                        {
                            var canliData = JsonSerializer.Deserialize<GelenBorussengerCanli>(mesaj);
                            if (canliData != null && canliData.mesaj != null)
                            {
                                // 1. C#'ın kendi hafızasına bu yeni mesajı ekle
                                if (!_csharpSohbetHafizasi.ContainsKey(canliData.peerID))
                                    _csharpSohbetHafizasi[canliData.peerID] = new List<P2PMesaj>();

                                _csharpSohbetHafizasi[canliData.peerID].Add(canliData.mesaj);

                                // 2. Eğer şu an C# arayüzünde o kişinin sohbeti açıksa canlı olarak ekrana bas!
                                // (Şu an _aktifBorussengerHedef hep null çünkü tıklama olayını daha yazmadık ama altyapı hazır)
                                if (_aktifBorussengerHedef == canliData.peerID)
                                {
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        bool bendenMi = canliData.mesaj.type == "me";
                                        SahteMesajEkle(canliData.mesaj.sender, canliData.mesaj.msg, bendenMi);
                                    });
                                }
                                System.Diagnostics.Debug.WriteLine($"🐺 [Börüssenger] CANLI MESAJ: {canliData.mesaj.sender}: {canliData.mesaj.msg}");
                            }
                        }
                        // İleride "Börüssenger'dan mesaj geldi" olayını da buraya "else if" ile ekleyeceğiz.
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Telsiz Hatası]: {ex.Message}");
                    }
                };
            });

            // "Tümünü Seç" kutusuna tıklanınca çalışacak motor
            ChkTumunuSec.CheckedChanged += (s, e) =>
            {
                bool hepsiSecilsinMi = e.Value;
                foreach (var asker in _canliTim.Values)
                {
                    asker.SeciliMi = hepsiSecilsinMi;
                }
                TimListesiniArayuzeCiz();
            };
            // 🔥 SORUN 2 ÇÖZÜLDÜ: KALPATIŞI (HEARTBEAT) MOTORUNU BAŞLAT 🔥
            PingMotorunuCalistir();
        }
        // ==========================================
        // 1.5 KALP ATIŞI (PING) MOTORU - 15 SANİYEDE BİR ÇALIŞIR
        // ==========================================
        private void PingMotorunuCalistir()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(15000); // 15 Saniye bekle

                    // Hata almamak için listeyi .ToList() ile kopyalayarak dönüyoruz
                    foreach (var asker in _canliTim.Values.ToList())
                    {
                        if (asker.Soket.IsAvailable)
                        {
                            asker.Soket.Send("1"); // Telsizden "1" (Ping) fırlat
                        }
                    }
                }
            });
        }

        // ==========================================
        // CANLI TİMİ ARAYÜZE (SOL PANELE) ÇİZ
        // ==========================================
        private void TimListesiniArayuzeCiz()
        {
            // Arayüz işlemleri mutlaka MainThread (Ana İş Parçacığı) üzerinde yapılmalıdır!
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SlAktifTim.Children.Clear(); // <-- BURASI DÜZELDİ

                if (_canliTim.Count == 0)
                {
                    SlAktifTim.Children.Add(new Label { Text = "Sahada aktif Börü yok...", TextColor = Colors.Gray, FontAttributes = FontAttributes.Italic, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 20, 0, 0) });
                    return;
                }

                foreach (var kvp in _canliTim)
                {
                    var asker = kvp.Value;
                    var guidId = kvp.Key;

                    // Duruma göre renk belirle
                    string durumRengi = asker.Durum == "Oyunda" ? "#4CAF50" : (asker.Durum.Contains("Lobi") ? "#FFC300" : "#F44336");
                    string durumMetni = asker.Durum == "Oyunda" ? $"🟢 Oyunda (Lvl {asker.Level})" : $"🟡 Lobide (Lvl {asker.Level})";

                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitionCollection {
                            new ColumnDefinition(GridLength.Auto), // Checkbox
                            new ColumnDefinition(GridLength.Auto), // Nokta
                            new ColumnDefinition(GridLength.Star), // Nick & Durum
                            new ColumnDefinition(GridLength.Auto)  // Altın
                        },
                        Padding = new Thickness(10),
                        BackgroundColor = Color.FromArgb("#2D2D2D"),
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    // Kenarları yuvarlatılmış kutu (Border) içine alıyoruz
                    var border = new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                        Content = grid
                    };

                    // 1. CheckBox (Seçim Kutusu)
                    var chk = new CheckBox { Color = Color.FromArgb("#4CAF50"), IsChecked = asker.SeciliMi, VerticalOptions = LayoutOptions.Center };
                    chk.CheckedChanged += (s, e) => { asker.SeciliMi = e.Value; }; // Kutuyu tikleyince hafızada da değişsin
                    grid.Children.Add(chk);
                    Grid.SetColumn(chk, 0);

                    // 2. Durum Noktası
                    var nokta = new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Color.FromArgb(durumRengi), VerticalOptions = LayoutOptions.Center, Margin = new Thickness(5, 0, 10, 0) };
                    grid.Children.Add(nokta);
                    Grid.SetColumn(nokta, 1);

                    // 3. İsim ve Durum Yazısı
                    var vStack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
                    vStack.Children.Add(new Label { Text = asker.Nick, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 14 });
                    vStack.Children.Add(new Label { Text = durumMetni, TextColor = Colors.Gray, FontSize = 11 });
                    grid.Children.Add(vStack);
                    Grid.SetColumn(vStack, 2);

                    // 4. Altın Miktarı
                    var lblAltin = new Label { Text = $"{asker.Altin} 🪙", TextColor = Color.FromArgb("#FFC300"), FontAttributes = FontAttributes.Bold, FontSize = 12, VerticalOptions = LayoutOptions.Center };
                    grid.Children.Add(lblAltin);
                    Grid.SetColumn(lblAltin, 3);

                    SlAktifTim.Children.Add(border); // <-- BURASI DÜZELDİ




                    // ==========================================
                    // 🔥 SAĞ TIK MENÜSÜ (NOT DEFTERİ) 🔥
                    // ==========================================
                    var sagTikMenu = new MenuFlyout();
                    var notButonu = new MenuFlyoutItem { Text = "📝 İstihbarat Notu Ekle/Gör" };

                    notButonu.Clicked += async (s, e) =>
                    {
                        // 1. Cihazın hafızasından bu botun (Nick'in) eski notunu çek. (Daha önce yazılmadıysa boş "" gelir)
                        string notAnahtari = $"Istihbarat_{asker.Nick}";
                        string mevcutNot = Preferences.Default.Get(notAnahtari, "");

                        string yeniNot = await App.Current.MainPage.DisplayPromptAsync(
                            title: $"🕵️ {asker.Nick} Dosyası",
                            message: "Bu ajan için notlarınızı buraya girin (P2P şifresi, ban durumu, görev vb.):",
                            accept: "Kaydet",
                            cancel: "İptal",
                            placeholder: "Örn: P2P Kodu 1453...",
                            maxLength: 500,
                            keyboard: Keyboard.Text,
                            initialValue: mevcutNot);

                        // 2. Eğer adam "İptal" tuşuna basmadıysa (yani yeniNot null değilse)
                        if (yeniNot != null)
                        {
                            // Yeni notu diske KALICI olarak beton gibi çak!
                            Preferences.Default.Set(notAnahtari, yeniNot);
                            System.Diagnostics.Debug.WriteLine($"[İSTİHBARAT KAYDEDİLDİ] {asker.Nick}: {yeniNot}");
                        }
                    };

                    sagTikMenu.Add(notButonu);

                    // 🔥 2. ASIL ÇÖZÜM: BORDER EKRANA TAMAMEN YÜKLENDİKTEN SONRA MENÜYÜ ZIMBALA
                    border.Loaded += (sender, args) =>
                    {
                        FlyoutBase.SetContextFlyout(border, sagTikMenu);
                    };
                }
            });
        }

        // ==========================================
        // ORTAK ATEŞLEME MOTORU (SADECE SEÇİLİ FİLOYA JSON FIRLAT)
        // ==========================================
        private void TimAskerlerineEmirGonder(object payload)
        {
            string jsonPaket = JsonSerializer.Serialize(payload);

            // Sadece hafızadaki "SeciliMi == true" olan botlara emri fırlat!
            foreach (var asker in _canliTim.Values.Where(x => x.SeciliMi))
            {
                if (asker.Soket.IsAvailable)
                {
                    asker.Soket.Send(jsonPaket);
                }
            }
        }



        // ==========================================
        // 3. ŞALTERLERİ VE BUTONLARI KODLARA BAĞLAMA
        // ==========================================
        private void ButonlariAteslemeyeBagla()
        {
            // --- HIZLI AKSİYONLAR ---
            BtnAltinCark.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "ALTIN_CARK_CEVIR" });
            BtnGulCark.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "GUL_CARK_CEVIR" });
            BtnKutuAc.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "KUTULARI_AC" });
            BtnDurumRaporu.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "DURUM_RAPORU_VER" });
            BtnSayfaYenile.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "SAYFAYI_YENILE" });
            BtnPanicMode.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "PANIC_MODE_TETIKLE" });

            // Acil Durdur (Kill Switch) - Rengini falan tasarladık, gücü burada
            BtnAcilDurdur.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "ACIL_DURDUR" });

            // --- TİM ANA MOTOR AYARLARI (DEV JSON PAKETİ) ---
            BtnAyarlariGonder.Clicked += (s, e) =>
            {
                // XAML'daki tüm Switch ve Entry değerlerini toplayıp devasa bir paket yapıyoruz
                var yeniAyarlar = new
                {
                    AUTO_PLAY = SwAutoPlay.IsToggled,
                    AUTO_REPLAY = SwAutoReplay.IsToggled,
                    AUTO_JOIN_ROOMS = SwAutoJoin.IsToggled,
                    AUTO_CREATE_ROOM = SwAutoCreate.IsToggled,
                    LOBBY_AUTO_QUIT_ACTIVE = SwLobbyQuit.IsToggled,
                    AUTO_JOIN_CASE_SENSITIVE = SwJoinCase.IsToggled,
                    DEBUG_MODE = SwDebug.IsToggled,
                    SHOW_HIDDEN_LVL = SwShowHidden.IsToggled,
                    TELEMETRY_ACTIVE = SwTelemetry.IsToggled,
                    CHAT_STATS = SwChatStats.IsToggled,
                    CHAT_SOUND = SwChatSound.IsToggled,

                    // String ve Numarik veriler
                    AUTO_JOIN_FILTER = EntJoinFilter.Text ?? "",
                    AUTO_JOIN_EXCLUDE = EntJoinExclude.Text ?? "",
                    AUTO_JOIN_PASSWORD = EntJoinPassword.Text ?? "",
                    AUTO_CREATE_TEMPLATE_NAME = EntCreateTemplate.Text ?? "",
                    USER_P2P_CODE = EntP2PCode.Text ?? "",

                    // Güvenli sayı çevirimi (Boşsa 0 olsun)
                    AUTO_SLOT = int.TryParse(EntSlot.Text, out int slot) ? slot : 0,
                    LOBBY_AUTO_QUIT_SECONDS = int.TryParse(EntLobbyQuit.Text, out int lQuit) ? lQuit : 0,
                    AUTO_REFRESH_INTERVAL = int.TryParse(EntRefresh.Text, out int refInt) ? refInt : 15,
                    WAITING_HOST_TIMEOUT = int.TryParse(EntHostTimeout.Text, out int hostTime) ? hostTime : 0
                };

                // Paketi JS tarafındaki "TUM_AYARLARI_GUNCELLE" emri formatında yolla
                TimAskerlerineEmirGonder(new
                {
                    komut = "TUM_AYARLARI_GUNCELLE",
                    ayarlar = yeniAyarlar
                });
            };

            // --- SAHA İLETİŞİMİ (OYUN İÇİ) ---
            BtnBioAyarla.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "BIO_AYARLA", yeniBio = EntYeniBio.Text ?? "" });
            // --- MODÜL 3: SAHA İLETİŞİMİ (OYUN İÇİ AKSİYONLAR) ---

            // 1. Oyun Chatine Mesaj Yazma (Gerçek Zamanlı Telsiz)
            BtnOyunChatYaz.Clicked += (s, e) =>
            {
                // Kutudaki yazıyı al
                string mesaj = EntOyunChat.Text;

                if (!string.IsNullOrWhiteSpace(mesaj))
                {
                    // Karargah emrini tüm seçili botlara fırlat
                    TimAskerlerineEmirGonder(new
                    {
                        komut = "BORU_CHATEMESAJYAZ",
                        mesaj = mesaj
                    });

                    // Gönderdikten sonra kutuyu temizle ve tekrar odaklan (seri yazım için)
                    EntOyunChat.Text = "";
                    EntOyunChat.Focus();

                    System.Diagnostics.Debug.WriteLine($"💬 [Karargah] Telsiz mesajı fırlatıldı: {mesaj}");
                }
            };

           

            // 2. Herkese Gül Atma (Toplu Füze)
            BtnHerkeseGul.Clicked += (s, e) =>
            {
                TimAskerlerineEmirGonder(new { komut = "BORU_HERKESEGULGONDER" });
            };

            // 3. Özel Gül Gönder (Hedef Seçmeli)
            BtnOzelGul.Clicked += async (s, e) =>
            {
                // Ekranda bir kutucuk açıp hedefi sorar (Slot no veya Nick)
                string hedef = await DisplayPromptAsync("🎯 Hedef Belirle", "Gül kime gitsin? (Slot No veya Nick):", "ATEŞLE", "İPTAL", "Örn: 5 veya Ahmet");

                if (!string.IsNullOrWhiteSpace(hedef))
                {
                    TimAskerlerineEmirGonder(new { komut = "BORU_GULGONDER", hedef = hedef });
                }
            };

            // 4. Random Emote
            BtnRandomEmote.Clicked += (s, e) =>
            {
                TimAskerlerineEmirGonder(new { komut = "BORU_RANDOMEMOTE" });
            };

            // --- BÖRÜSSENGER (P2P İSTİHBARAT) ---
            BtnP2PYedekle.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "BORUSSENGER_MESAJLARI_YEDEKLE" });
            BtnP2PSil.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "BORUSSENGER_MESAJLARI_SIL" });
            BtnP2PSync.Clicked += (s, e) => TimAskerlerineEmirGonder(new { komut = "BORUSSENGER_MESAJLARI_SYNCLE" });


            // ==========================================
            // 🔥 AYAR ŞABLONU SİSTEMİ (KAYDET & YÜKLE) 🔥
            // ==========================================

            // İlk açılışta PC'nin hafızasından eski şablonları çek
            string eskiSablonlarJson = Preferences.Default.Get("Boru_AyarSablonlari", "");
            if (!string.IsNullOrEmpty(eskiSablonlarJson))
            {
                _kayitliSablonlar = JsonSerializer.Deserialize<Dictionary<string, AyarSablonu>>(eskiSablonlarJson);
                PickerSablonlar.ItemsSource = _kayitliSablonlar.Keys.ToList();
            }

            // KAYDET TUŞU
            BtnSablonKaydet.Clicked += async (s, e) =>
            {
                string sablonAdi = await DisplayPromptAsync("Şablon Kaydet", "Bu ayar kombinasyonuna bir isim verin:", "Kaydet", "İptal", "Örn: Hızlı XP Lobi Modu");
                if (string.IsNullOrWhiteSpace(sablonAdi)) return;

                var yeniSablon = new AyarSablonu
                {
                    AutoPlay = SwAutoPlay.IsToggled,
                    AutoReplay = SwAutoReplay.IsToggled,
                    AutoJoin = SwAutoJoin.IsToggled,
                    AutoCreate = SwAutoCreate.IsToggled,
                    LobbyQuit = SwLobbyQuit.IsToggled,
                    JoinCase = SwJoinCase.IsToggled,
                    Debug = SwDebug.IsToggled,
                    ShowHidden = SwShowHidden.IsToggled,
                    Telemetry = SwTelemetry.IsToggled,
                    ChatStats = SwChatStats.IsToggled,
                    ChatSound = SwChatSound.IsToggled,
                    JoinFilter = EntJoinFilter.Text,
                    JoinExclude = EntJoinExclude.Text,
                    JoinPassword = EntJoinPassword.Text,
                    CreateTemplate = EntCreateTemplate.Text,
                    Slot = EntSlot.Text,
                    P2PCode = EntP2PCode.Text,
                    LobbyQuitSec = EntLobbyQuit.Text,
                    RefreshMin = EntRefresh.Text,
                    HostTimeout = EntHostTimeout.Text
                };

                _kayitliSablonlar[sablonAdi] = yeniSablon;
                Preferences.Default.Set("Boru_AyarSablonlari", JsonSerializer.Serialize(_kayitliSablonlar)); // Diske kalıcı kaydet

                PickerSablonlar.ItemsSource = _kayitliSablonlar.Keys.ToList();
                PickerSablonlar.SelectedItem = sablonAdi;
            };

            // YÜKLE TUŞU
            BtnSablonYukle.Clicked += (s, e) =>
            {
                if (PickerSablonlar.SelectedItem == null) return;
                string secilen = PickerSablonlar.SelectedItem.ToString();

                if (_kayitliSablonlar.TryGetValue(secilen, out var sablon))
                {
                    SwAutoPlay.IsToggled = sablon.AutoPlay; SwAutoReplay.IsToggled = sablon.AutoReplay;
                    SwAutoJoin.IsToggled = sablon.AutoJoin; SwAutoCreate.IsToggled = sablon.AutoCreate;
                    SwLobbyQuit.IsToggled = sablon.LobbyQuit; SwJoinCase.IsToggled = sablon.JoinCase;
                    SwDebug.IsToggled = sablon.Debug; SwShowHidden.IsToggled = sablon.ShowHidden;
                    SwTelemetry.IsToggled = sablon.Telemetry; SwChatStats.IsToggled = sablon.ChatStats; SwChatSound.IsToggled = sablon.ChatSound;
                    EntJoinFilter.Text = sablon.JoinFilter; EntJoinExclude.Text = sablon.JoinExclude;
                    EntJoinPassword.Text = sablon.JoinPassword; EntCreateTemplate.Text = sablon.CreateTemplate;
                    EntSlot.Text = sablon.Slot; EntP2PCode.Text = sablon.P2PCode;
                    EntLobbyQuit.Text = sablon.LobbyQuitSec; EntRefresh.Text = sablon.RefreshMin; EntHostTimeout.Text = sablon.HostTimeout;
                }
            };



        }



        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // 1. Ayarı cihazın hafızasından yükle (Varsayılan 0, yani "Asla" seçili gelsin)
            int kayitliIndex = Preferences.Default.Get(ConfigBakimAyar, 0);
            PickerBakim.SelectedIndex = kayitliIndex;

            // 2. Eğer "Asla" (0) DEĞİLSE temizlik yap
            if (kayitliIndex > 0)
            {
                int silinecekGun = kayitliIndex == 1 ? 3 : (kayitliIndex == 2 ? 7 : 30);
                await _dbService.EskiVerileriTemizleAsync(silinecekGun);
            }

            await FiltreleriDoldurAsync();
            await GrafikleriGuncelleAsync(); // <--- İLK AÇILIŞTA EKRANI DOLDURMASI İÇİN
        }

        private void OnGizlilikToggled(object sender, ToggledEventArgs e)
        {
            IsGizlilikModuAcik = e.Value; // Statik değişkeni güncelle

            // Tabloyu tetiklemek için listeyi bir kez yenile (UI refresh)
            var mevcut = GelenVeriler.ToList();
            GelenVeriler.Clear();
            foreach (var item in mevcut) GelenVeriler.Add(item);
        }


        private async void GecmisVerileriYukle()
        {
            try
            {
                // Eğer veritabanı henüz hazır değilse biraz beklemesini sağlıyoruz
                await Task.Delay(500);
                var veriler = await _dbService.SonVerileriGetirAsync();
                foreach (var veri in veriler)
                {
                    GelenVeriler.Add(veri);
                }
                // 🔥 KONTAK ÇEVRİLDİ 1: Geçmiş veriler yüklendikten sonra kartları doldur!
                await KpiKartlariniGuncelleAsync();
                await SaatlikIsiHaritasiniGuncelleAsync(); // <--- BUNU EKLE
                await GrafikleriGuncelleAsync(); // <--- BU BURADA OLMALI
                AnalizleriHesapla();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GEÇMİŞ VERİ YÜKLEME HATASI: {ex.Message}");
            }
        }
        private async Task FiltreleriDoldurAsync()
        {
            try
            {
                var veriler = await _dbService.SonVerileriGetirAsync();

                // Benzersiz hesapları ve rolleri bul
                var hesaplar = veriler.Select(x => x.accountnick).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                hesaplar.Insert(0, "Tüm Hesaplar");

                var roller = veriler.Select(x => x.playedRole).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                roller.Insert(0, "Tüm Roller");

                // ARAYÜZÜ GÜNCELLEME İŞLEMİNİ ANA THREAD'E ALIYORUZ (Kasıntıyı çözen sihirli kısım)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PickerHesap.ItemsSource = hesaplar;
                    PickerHesap.SelectedIndex = 0;

                    PickerRol.ItemsSource = roller;
                    PickerRol.SelectedIndex = 0;
                });

                // Veritabanındaki benzersiz maç sonuçlarını (Win, Loss, over&&Bilinmiyor vs.) bul
                var sonuclar = veriler.Select(x => x.matchResult).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                sonuclar.Insert(0, "Tüm Sonuçlar");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Eski kodlar (PickerHesap ve PickerRol) burada kalacak...

                    // Maç sonucu ve zaman aralığı picker'larını bağla
                    PickerSonuc.ItemsSource = sonuclar;
                    PickerSonuc.SelectedIndex = 0;
                    PickerZaman.SelectedIndex = 0; // Varsayılan olarak "Tüm Zamanlar" seçili gelsin
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ FİLTRE DOLDURMA HATASI: {ex.Message}");
            }
        }

        // --- SEKME GEÇİŞ MOTORU ---
        private void OnAnalizSekmesiClicked(object sender, EventArgs e)
        {
            AnalizEkrani.IsVisible = true; YonetimEkrani.IsVisible = false; AyarlarEkrani.IsVisible = false;
            BtnAnalizSekmesi.BackgroundColor = Color.FromArgb("#4CAF50"); BtnAnalizSekmesi.TextColor = Colors.White;
            BtnYonetimSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnYonetimSekmesi.TextColor = Colors.Gray;
            BtnAyarlarSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnAyarlarSekmesi.TextColor = Colors.Gray;
        }

        private void OnYonetimSekmesiClicked(object sender, EventArgs e)
        {
            AnalizEkrani.IsVisible = false; YonetimEkrani.IsVisible = true; AyarlarEkrani.IsVisible = false;
            BtnYonetimSekmesi.BackgroundColor = Color.FromArgb("#4CAF50"); BtnYonetimSekmesi.TextColor = Colors.White;
            BtnAnalizSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnAnalizSekmesi.TextColor = Colors.Gray;
            BtnAyarlarSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnAyarlarSekmesi.TextColor = Colors.Gray;
        }

        private void OnAyarlarSekmesiClicked(object sender, EventArgs e)
        {
            AnalizEkrani.IsVisible = false; YonetimEkrani.IsVisible = false; AyarlarEkrani.IsVisible = true;
            BtnAyarlarSekmesi.BackgroundColor = Color.FromArgb("#4CAF50"); BtnAyarlarSekmesi.TextColor = Colors.White;
            BtnAnalizSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnAnalizSekmesi.TextColor = Colors.Gray;
            BtnYonetimSekmesi.BackgroundColor = Color.FromArgb("#2D2D2D"); BtnYonetimSekmesi.TextColor = Colors.Gray;
        }
        // --- VERİTABANI SIFIRLAMA MOTORU ---
        private async void OnVeritabaniniSifirlaClicked(object sender, EventArgs e)
        {
            // Adama son bir kez "Emin misin?" diye soruyoruz ki kaza çıkmasın
            bool eminMisin = await DisplayAlert("⚠️ Kritik Uyarı", "Tüm geçmiş maç verileri, analizler ve kayıtlar KALICI olarak silinecek. Emin misin?", "Evet, Tamamen Sil", "İptal");

            if (eminMisin)
            {
                try
                {
                    // 1. Veritabanını nükleerle
                    await _dbService.TumVerileriSilAsync();

                    // 2. Arayüzdeki tabloyu ve filtreleri boşalt
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GelenVeriler.Clear();
                    });

                    // 3. KPI Kartlarını sıfırla (veritabanı boş olduğu için hepsi sıfırlanacak)
                    await KpiKartlariniGuncelleAsync();

                    // 4. Müşteriye bilgi ver
                    await DisplayAlert("✅ Başarılı", "Veritabanı tertemiz oldu. Yeni veriler bekleniyor.", "Tamam");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("❌ Hata", $"Sıfırlama sırasında bir sorun oluştu: {ex.Message}", "Tamam");
                }
            }
        }
        private async void OnFiltreUygulaClicked(object sender, EventArgs e)
        {
            if (int.TryParse(EntryHedefLevel.Text, out int h)) GlobalHedefLevel = h;
            // SQLite'dan tüm geçmişi çek
            var tumVeriler = await _dbService.SonVerileriGetirAsync();

            // 1. Hesap Filtresi
            if (PickerHesap.SelectedItem != null && PickerHesap.SelectedItem.ToString() != "Tüm Hesaplar")
            {
                string secilenHesap = PickerHesap.SelectedItem.ToString();
                tumVeriler = tumVeriler.Where(x => x.accountnick == secilenHesap).ToList();
            }

            // 2. Platform Filtresi
            if (PickerPlatform.SelectedItem != null && PickerPlatform.SelectedItem.ToString() != "Tümü")
            {
                bool mobilMiAraniyor = PickerPlatform.SelectedItem.ToString().Contains("Mobil");
                tumVeriler = tumVeriler.Where(x => x.isMobile == mobilMiAraniyor).ToList();
            }
            // 3. Maç Sonucu Filtresi
            if (PickerSonuc.SelectedItem != null && PickerSonuc.SelectedItem.ToString() != "Tüm Sonuçlar")
            {
                string secilenSonuc = PickerSonuc.SelectedItem.ToString();
                tumVeriler = tumVeriler.Where(x => x.matchResult == secilenSonuc).ToList();
            }

            // 4. Zaman Aralığı Filtresi (Matematik kısmı)
            if (PickerZaman.SelectedItem != null && PickerZaman.SelectedItem.ToString() != "Tüm Zamanlar")
            {
                long suAnkiZaman = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string secilenZaman = PickerZaman.SelectedItem.ToString();

                if (secilenZaman == "Son 1 Saat")
                    tumVeriler = tumVeriler.Where(x => x.timestamp >= suAnkiZaman - (3600 * 1000)).ToList();
                else if (secilenZaman == "Bugün")
                    tumVeriler = tumVeriler.Where(x => x.timestamp >= suAnkiZaman - (86400 * 1000)).ToList(); // Son 24 saat
                else if (secilenZaman == "Son 7 Gün")
                    tumVeriler = tumVeriler.Where(x => x.timestamp >= suAnkiZaman - (7L * 86400 * 1000)).ToList();
            }
            // 5. YENİ: Sıralama Ölçütü Mantığı
            if (PickerSiralama.SelectedItem != null)
            {
                string secilenSiralama = PickerSiralama.SelectedItem.ToString();
                switch (secilenSiralama)
                {
                    case "⏱️ En Yeniler (Varsayılan)":
                        tumVeriler = tumVeriler.OrderByDescending(x => x.Id).ToList();
                        break;
                    case "⏱️ En Eskiler":
                        tumVeriler = tumVeriler.OrderBy(x => x.Id).ToList();
                        break;
                    case "🔠 Nick (A-Z)":
                        tumVeriler = tumVeriler.OrderBy(x => x.accountnick).ToList();
                        break;
                    case "⭐ Level (Yüksekten Düşüğe)":
                        tumVeriler = tumVeriler.OrderByDescending(x => x.currentLevel).ToList();
                        break;
                    case "💰 Kazanım (Çoktan Aza)":
                        tumVeriler = tumVeriler.OrderByDescending(x => x.xpGained).ToList();
                        break;
                    case "⏳ Süre (Uzundan Kısaya)":
                        tumVeriler = tumVeriler.OrderByDescending(x => x.matchDurationSec).ToList();
                        break;
                }
            }
            else
            {
                // Kutudan hiçbir şey seçilmemişse varsayılan olarak en yenileri en üste koy
                tumVeriler = tumVeriler.OrderByDescending(x => x.Id).ToList();
            }

            // Arayüzdeki listeyi temizle ve filtrelenmiş verileri doldur
            GelenVeriler.Clear();
            foreach (var veri in tumVeriler)
            {
                GelenVeriler.Add(veri);
            }
        }
        private async void AnalizleriHesapla()
        {
            var tumVeriler = await _dbService.SonVerileriGetirAsync();

            // Sadece son 1 saatte gelen verileri filtrele
            long birSaatOnce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (60 * 60 * 1000);
            var sonBirSaatVerileri = tumVeriler.Where(x => x.timestamp >= birSaatOnce).ToList();

            // Verileri hesap ismine göre grupla ve kimin ne kadar XP kastığını topla
            var hesapBazliXp = sonBirSaatVerileri
                .GroupBy(x => x.accountnick)
                .Select(grup => new
                {
                    HesapAdi = grup.Key,
                    ToplamXP = grup.Sum(x => x.xpGained),
                    MacSayisi = grup.Count()
                })
                .OrderByDescending(x => x.ToplamXP) // En çok kasanı en üste al
                .ToList();

            foreach (var hesap in hesapBazliXp)
            {
                System.Diagnostics.Debug.WriteLine($"🏆 [SON 1 SAAT] {hesap.HesapAdi}: {hesap.ToplamXP} XP ({hesap.MacSayisi} Maç)");
            }
        }
        // Bu metot, yeni bir veri geldiğinde "Acaba bu rol/hesap filtrede var mı?" diye bakar, yoksa anında ekler.
        private void FiltreyeDinamikEkle(Picker picker, string yeniDeger)
        {
            if (string.IsNullOrWhiteSpace(yeniDeger)) return;

            // Mevcut listeyi al
            var mevcutListe = picker.ItemsSource?.Cast<string>().ToList() ?? new List<string>();

            // Eğer bu yeni rol/hesap listede yoksa ekle
            if (!mevcutListe.Contains(yeniDeger))
            {
                var seciliIndex = picker.SelectedIndex; // Kullanıcının o anki seçimini hafızaya al

                mevcutListe.Add(yeniDeger);

                picker.ItemsSource = null; // Arayüzün yenilendiğini anlaması için reset atıyoruz
                picker.ItemsSource = mevcutListe;
                picker.SelectedIndex = seciliIndex; // Seçimi geri yüklüyoruz ki adam filtreye bakarken liste kaymasın
            }
        }
        private async Task KpiKartlariniGuncelleAsync()
        {
            try
            {
                var tumVeriler = await _dbService.SonVerileriGetirAsync();
                if (tumVeriler == null || !tumVeriler.Any()) return;

                long suAn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long birSaatOnce = suAn - (3600 * 1000);
                long yirmiDortSaatOnce = suAn - (86400 * 1000);

                // --- 1. ESKİ KARTLARIN HESAPLAMALARI ---
                var hesaplarinSonDurumu = tumVeriler.GroupBy(x => x.accountnick).Select(g => g.OrderByDescending(y => y.Id).First()).ToList();
                int toplamKasa = hesaplarinSonDurumu.Sum(x => x.totalgold);
                int satisaHazirSayisi = hesaplarinSonDurumu.Count(x => x.currentLevel >= GlobalHedefLevel);

                var gecerliSureler = tumVeriler.Where(x => x.matchDurationSec > 0 && x.matchDurationSec < 10000).ToList();
                int ortalamaSure = gecerliSureler.Any() ? (int)gecerliSureler.Average(x => x.matchDurationSec) : 0;

                var rolIstatistikleri = tumVeriler.Where(x => !string.IsNullOrWhiteSpace(x.playedRole)).GroupBy(x => x.playedRole)
                    .Select(g => new { Rol = g.Key, OrtalamaXp = g.Average(y => y.xpGained) }).OrderByDescending(x => x.OrtalamaXp).FirstOrDefault();

                // --- 2. YENİ KARTLARIN HESAPLAMALARI ---
                var son1SaatVeri = tumVeriler.Where(x => x.timestamp >= birSaatOnce).ToList();
                var son24SaatVeri = tumVeriler.Where(x => x.timestamp >= yirmiDortSaatOnce).ToList();

                // Aktif Filo
                int aktifBotSayisi = son1SaatVeri.Select(x => x.accountnick).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count();

                // XPM (Motor Hızı) - Son 1 saatteki geçerli maçların toplam XP'si / Toplam Dakika
                var hizVerileri = son1SaatVeri.Where(x => x.matchDurationSec > 0 && x.matchDurationSec < 10000).ToList();
                double toplamDakika1Saat = hizVerileri.Sum(x => x.matchDurationSec) / 60.0;
                int ortalamaXpm = toplamDakika1Saat > 0 ? (int)(hizVerileri.Sum(x => x.xpGained) / toplamDakika1Saat) : 0;

                // 24 Saatlik Hasat
                int xp24Saat = son24SaatVeri.Sum(x => x.xpGained);
                int mac24Saat = son24SaatVeri.Count;

                // Günün Şampiyonu (MVP)
                var mvpBot = son24SaatVeri.Where(x => !string.IsNullOrEmpty(x.accountnick))
                    .GroupBy(x => x.accountnick)
                    .Select(g => new { Nick = g.Key, KastigiXp = g.Sum(v => v.xpGained) })
                    .OrderByDescending(x => x.KastigiXp).FirstOrDefault();

                // --- 3. ARAYÜZE YAZDIRMA (Ana Thread) ---
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LblToplamAltin.Text = $"{toplamKasa:N0}";
                    LblSatisaHazir.Text = "Vay be!";
                    LblOrtalamaSure.Text = ortalamaSure > 0 ? $"{ortalamaSure} Saniye" : "Veri Bekleniyor";

                    if (rolIstatistikleri != null)
                    {
                        LblEnKarliRol.Text = rolIstatistikleri.Rol;
                        LblEnKarliRolDetay.Text = $"Maç Başı: +{(int)rolIstatistikleri.OrtalamaXp} XP";
                    }

                    // Yeni Kartlar
                    LblAktifBot.Text = aktifBotSayisi.ToString();
                    LblSistemDurumu.Text = aktifBotSayisi > 0 ? "🟢 Sistem Stabil (Veri Akıyor)" : "🔴 Tüm Botlar Uykuda";
                    LblSistemDurumu.TextColor = aktifBotSayisi > 0 ? Color.FromArgb("#4CAF50") : Colors.Orange;

                    LblOrtalamaXPM.Text = ortalamaXpm > 0 ? $"{ortalamaXpm} XP/dk" : "0 XP/dk";

                    Lbl24SaatXp.Text = $"+{xp24Saat:N0} XP";
                    Lbl24SaatMac.Text = $"Analiz Edilen: {mac24Saat} Maç";

                    if (mvpBot != null)
                    {
                        LblSampiyonNick.Text = mvpBot.Nick;
                        LblSampiyonXp.Text = $"Bugün kasan: +{mvpBot.KastigiXp:N0} XP";
                    }
                    else
                    {
                        LblSampiyonNick.Text = "Belirsiz";
                        LblSampiyonXp.Text = "Henüz veri yok";
                    }
                });
                // --- HEDEF HESAPLAMA MOTORU (RXZ STYLE) ---
                if (int.TryParse(EntryHedefLevel.Text, out int hedefLvl))
                {
                    // Ortalamaları al (XPM - Dakika başı XP)
                    // Örnek: Botun dakikada kastığı XP (ortalamaXpm değişkenini zaten hesaplamıştık)
                    double botXpm = ortalamaXpm > 0 ? ortalamaXpm : 1;
                    double manuelXpm = 1300 / 60.0; // Manuel oynayan birinin ortalama hızı (Saatte 1300 XP)

                    // Filonun ortalama level'ını bul
                    double ortalamaMevcutLvl = hesaplarinSonDurumu.Average(x => x.currentLevel);

                    if (hedefLvl > ortalamaMevcutLvl)
                    {
                        // Kaç XP lazım? (Wolvesville yaklaşık formülü: Her level arası 2000 XP varsayalım)
                        double gerekenXp = (hedefLvl - ortalamaMevcutLvl) * 2000;

                        // Süreleri hesapla (Dakika cinsinden)
                        double botDk = gerekenXp / botXpm;
                        double manuelDk = gerekenXp / manuelXpm;
                        double tasarrufDk = manuelDk - botDk;

                        // Arayüze yazdır (Saat cinsinden)
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            TimeSpan t = TimeSpan.FromMinutes(tasarrufDk);
                            LblKazanilanZaman.Text = tasarrufDk > 60
                                ? $"{(int)t.TotalHours} Saat {t.Minutes} Dk"
                                : $"{(int)tasarrufDk} Dakika";

                            LblHedefDurum.Text = $"Lvl {hedefLvl} için bot {(int)(botDk / 60)}s çalışacak";
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LblKazanilanZaman.Text = "Hedefe Ulaşıldı";
                            LblHedefDurum.Text = "Botlar mezun oldu!";
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ KPI GÜNCELLEME HATASI: {ex.Message}");
            }
        }






        private async void OnMenuKopyalaClicked(object sender, EventArgs e)
        {
            var menuDegeri = (MenuItem)sender;
            string kopyalanacakNick = menuDegeri.CommandParameter?.ToString();

            if (!string.IsNullOrEmpty(kopyalanacakNick))
            {
                await Clipboard.Default.SetTextAsync(kopyalanacakNick);
                // İstersen buraya ufak bir "Kopyalandı" uyarısı da atabiliriz ileride
            }
        }

        private void OnMenuSilClicked(object sender, EventArgs e)
        {
            var menuDegeri = (MenuItem)sender;
            string silinecekNick = menuDegeri.CommandParameter?.ToString();

            // Şimdilik sadece arayüzden gizliyoruz, veritabanından kalıcı silme işini Ayarlar sekmesine bırakacağız
            var silinecekler = GelenVeriler.Where(x => x.accountnick == silinecekNick).ToList();
            foreach (var item in silinecekler)
            {
                GelenVeriler.Remove(item);
            }
        }

        // YENİ METOT: Veritabanından kökünü kazıyan motor
        private async void OnMenuKaliciSilClicked(object sender, EventArgs e)
        {
            var menuDegeri = (MenuItem)sender;

            // XAML'dan Binding . ile gönderdiğimiz o satırın tamamını yakalıyoruz
            if (menuDegeri.CommandParameter is BoruPayload silinecekVeri)
            {
                // 1. Veritabanından kalıcı olarak sil
                await _dbService.VeriSilAsync(silinecekVeri.Id);

                // 2. Arayüzdeki tablodan anında kaldır
                GelenVeriler.Remove(silinecekVeri);

                // 3. Yanlış veri grafiklerin ortalamasını bozmuş olabilir, kartları ve grafikleri yenile!
                await KpiKartlariniGuncelleAsync();
                await SaatlikIsiHaritasiniGuncelleAsync();
                await GrafikleriGuncelleAsync();
            }
        }


        private void OnPickerBakimChanged(object sender, EventArgs e)
        {
            // Adam menüden "Asla" veya "7 Gün" seçtiği an bunu telefona/PC'ye kaydet
            Preferences.Default.Set(ConfigBakimAyar, PickerBakim.SelectedIndex);
        }

        private async Task SaatlikIsiHaritasiniGuncelleAsync()
        {
            try
            {
                var tumVeriler = await _dbService.SonVerileriGetirAsync();
                if (tumVeriler == null || !tumVeriler.Any()) return;

                // Sadece son 7 günü ve hatalı olmayan (süresi 0 veya 10000 üstü olmayan) maçları al
                long yediGunOnce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (7L * 86400 * 1000);
                var son7GunVeri = tumVeriler.Where(x => x.timestamp >= yediGunOnce && x.matchDurationSec > 0 && x.matchDurationSec < 10000).ToList();

                if (!son7GunVeri.Any())
                {
                    MainThread.BeginInvokeOnMainThread(() => {
                        StackSaatAnaliz.Children.Clear();
                        StackSaatAnaliz.Children.Add(new Label { Text = "Yeterli veri yok. Botlar biraz çalışsın...", TextColor = Colors.Gray });
                    });
                    return;
                }

                // Saatlere göre grupla (Yerel saate çevirerek) ve Ortalama XP/Saat hesapla
                var saatRaporu = son7GunVeri
                    .GroupBy(x => DateTimeOffset.FromUnixTimeMilliseconds(x.timestamp).ToLocalTime().Hour)
                    .Select(g => new {
                        Saat = g.Key,
                        // Toplam kazanılan XP / Toplam geçen dakika = XPM. Bunu 60 ile çarpıp saatlik hızı buluyoruz.
                        XpHizi = (g.Sum(x => x.xpGained) / (g.Sum(x => x.matchDurationSec) / 60.0)) * 60
                    })
                    .OrderByDescending(x => x.XpHizi) // En yüksek XP/Saat hızına göre sırala
                    .Take(24) // En iyi 24 saati al
                    .ToList();

                MainThread.BeginInvokeOnMainThread(() => {
                    StackSaatAnaliz.Children.Clear();
                    foreach (var s in saatRaporu)
                    {
                        // Her saat için şık bir mini kart oluşturuyoruz
                        var frame = new Border
                        {
                            BackgroundColor = Color.FromArgb("#2D2D2D"),
                            StrokeThickness = 1,
                            Stroke = Color.FromArgb("#FF9800"),
                            Padding = new Thickness(15, 10),
                            StrokeShape = new RoundRectangle { CornerRadius = 8 }
                        };

                        var vStack = new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center };
                        vStack.Children.Add(new Label { Text = $"{s.Saat:00}:00 - {s.Saat + 1:00}:00", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center });
                        vStack.Children.Add(new Label { Text = $"{(int)s.XpHizi:N0} XP/saat", TextColor = Color.FromArgb("#4CAF50"), FontSize = 12, HorizontalOptions = LayoutOptions.Center });

                        frame.Content = vStack;
                        StackSaatAnaliz.Children.Add(frame);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ISI HARİTASI HATASI: {ex.Message}");
            }
        }
        private async Task GrafikleriGuncelleAsync()
        {
            try
            {
                var tumVeriler = await _dbService.SonVerileriGetirAsync();
                if (tumVeriler == null || !tumVeriler.Any()) return;

                // --- 1. XP BAR VERİLERİ (Son 12 geçerli maç) ---
                var sonMaclar = tumVeriler.Where(x => x.xpGained > 0).OrderByDescending(x => x.timestamp).Take(12).Reverse().ToList();

                // En yüksek XP'yi bulalım ki çubukların boyunu ona göre orantılayalım
                int maxXp = sonMaclar.Any() ? sonMaclar.Max(x => x.xpGained) : 1;
                if (maxXp == 0) maxXp = 1; // 0'a bölme hatası yememek için

                // --- 2. WIN / LOSS VERİLERİ ---
                int winSayisi = tumVeriler.Count(x => !string.IsNullOrEmpty(x.matchResult) && x.matchResult.ToLower().Contains("win"));
                int lossSayisi = tumVeriler.Count(x => !string.IsNullOrEmpty(x.matchResult) || x.matchResult.ToLower().Contains("loss"));

                int toplamMac = winSayisi + lossSayisi;
                if (toplamMac == 0) toplamMac = 1;

                double winYuzdesi = (double)winSayisi / toplamMac;

                // --- 3. EKRANA ÇİZİM (Ana İş Parçacığı) ---
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // XP Çubuklarını Çiz
                    StackXpBarlar.Children.Clear();
                    foreach (var mac in sonMaclar)
                    {
                        // Çubuğun yüksekliğini 100 birim üzerinden orantılıyoruz
                        double barYuksekligi = ((double)mac.xpGained / maxXp) * 100;
                        if (barYuksekligi < 5) barYuksekligi = 5; // En azından dibi görünsün

                        var barStack = new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Spacing = 5 };

                        // Çubuğun tepesindeki XP yazısı
                        var lblXp = new Label { Text = mac.xpGained.ToString(), TextColor = Colors.LightGray, FontSize = 10, HorizontalOptions = LayoutOptions.Center };

                        // Çubuğun ta kendisi (Sadece üst köşeleri yuvarlatılmış)
                        var bar = new Border
                        {
                            BackgroundColor = Color.FromArgb("#00BCD4"),
                            WidthRequest = 20,
                            HeightRequest = barYuksekligi,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) },
                            StrokeThickness = 0
                        };

                        barStack.Children.Add(lblXp);
                        barStack.Children.Add(bar);
                        StackXpBarlar.Children.Add(barStack);
                    }

                    // Win/Loss Çubuğunu Güncelle
                    LblWinSayisi.Text = winSayisi.ToString();
                    LblLossSayisi.Text = lossSayisi.ToString();

                    // Kutunun toplam genişliği ortalama 260 birim. Oranı ona göre dağıtıyoruz.
                    double barToplamGenislik = 260;
                    BarWin.WidthRequest = barToplamGenislik * winYuzdesi;
                    BarLoss.WidthRequest = barToplamGenislik * (1 - winYuzdesi);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GRAFİK ÇİZİM HATASI: {ex.Message}");
            }
        }


        
        // Börüssenger Açma Butonu Tıklanınca
        private void OnBorussengerAcClicked(object sender, EventArgs e)
        {
            // 1. Pencereyi Aç ve Sahte Her Şeyi Temizle!
            BorussengerEkrani.IsVisible = true;
            SlKisiler.Children.Clear();
            SlSohbetGecmisi.Children.Clear();
            LblSohbetHedef.Text = "Kişi Seçin";

            // 2. Sol "Aktif Tim" listesinden Checkbox'ı işaretli olan İLK botu bul
            var seciliBot = _canliTim.Values.FirstOrDefault(x => x.SeciliMi);

            if (seciliBot != null)
            {
                SlKisiler.Children.Add(new Label { Text = "📡 İstihbarat çekiliyor...", TextColor = Colors.Gray, FontAttributes = FontAttributes.Italic, Margin = new Thickness(15) });

                // Seçili bota emir fırlat: "Bana rehberini ve mesajlarını gönder!"
                // (Bu komut JS'teki restoreMessages() fonksiyonunu tetikleyecek)
                TimAskerlerineEmirGonder(new { komut = "BORUSSENGER_MESAJLARI_SYNCLE" });
            }
            else
            {
                SlKisiler.Children.Add(new Label { Text = "⚠️ Önce sol listeden bir ajan seç!", TextColor = Colors.Red, FontAttributes = FontAttributes.Bold, Margin = new Thickness(15) });
            }
        }

        // Börüssenger Kapatma Butonu Tıklanınca
        private void OnBorussengerKapatClicked(object sender, EventArgs e)
        {
            BorussengerEkrani.IsVisible = false;
        }

        // --- YARDIMCI METOTLAR (EKRANA ÇİZİM YAPANLAR) ---

        private void SahteKisiEkle(string isim, string durumRengi, bool seciliMi)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                Padding = new Thickness(15, 12),
                BackgroundColor = seciliMi ? Color.FromArgb("#37373D") : Colors.Transparent
            };

            // Sol Taraf Seçili Çizgisi (Kırmızı çizgi)
            if (seciliMi)
            {
                grid.Children.Add(new BoxView { WidthRequest = 3, Color = Color.FromArgb("#FB2E00"), HorizontalOptions = LayoutOptions.Start });
            }

            var stack = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
            stack.Children.Add(new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Color.FromArgb(durumRengi), VerticalOptions = LayoutOptions.Center });
            stack.Children.Add(new Label { Text = isim, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 13, VerticalOptions = LayoutOptions.Center });

            grid.Children.Add(stack);
            Grid.SetColumn(stack, 1);

            SlKisiler.Children.Add(grid);
        }

        private void SahteMesajEkle(string gonderen, string mesaj, bool bendenMi)
        {
            var border = new Border
            {
                BackgroundColor = bendenMi ? Color.FromArgb("#941B00") : Color.FromArgb("#3E3E42"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                Padding = new Thickness(10, 8),
                HorizontalOptions = bendenMi ? LayoutOptions.End : LayoutOptions.Start,
                MaximumWidthRequest = 400
            };

            var stack = new VerticalStackLayout();

            // Gönderen İsmi (Karşı tarafsa yazdır)
            if (!bendenMi)
            {
                stack.Children.Add(new Label { Text = gonderen, TextColor = Color.FromArgb("#FB2E00"), FontSize = 11, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 5) });
            }

            // ==========================================
            // 🔥 İSTİHBARAT FİLTRESİ (HTML & BASE64 ÇÖZÜCÜ) 🔥
            // ==========================================
            try
            {
                // 1. EĞER GELEN MESAJ BİR RESİMSE:
                if (mesaj.Contains("<img") && mesaj.Contains("base64,"))
                {
                    // HTML'in içindeki o iğrenç Base64 kodunu cımbızla çekip çıkartıyoruz
                    int startIndex = mesaj.IndexOf("base64,") + 7;
                    int endIndex = mesaj.IndexOf("\"", startIndex);
                    if (endIndex == -1) endIndex = mesaj.IndexOf("'", startIndex);

                    string base64Data = mesaj.Substring(startIndex, endIndex - startIndex);

                    // Kodu gerçek bir görsele (Byte dizisine) çeviriyoruz
                    byte[] imageBytes = Convert.FromBase64String(base64Data);

                    // C# Karargahında kanlı canlı resmi çiziyoruz!
                    var image = new Image
                    {
                        Source = ImageSource.FromStream(() => new MemoryStream(imageBytes)),
                        HeightRequest = 180, // Ekranda çok devasa durmasın diye sınırlandırdık
                        Aspect = Aspect.AspectFit,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    stack.Children.Add(new Label { Text = "📷 Görsel İstihbaratı:", TextColor = Colors.LightGray, FontSize = 11, FontAttributes = FontAttributes.Italic });
                    stack.Children.Add(image);
                }
                // 2. EĞER GELEN MESAJ BİR VİDEOYSA:
                else if (mesaj.Contains("<video"))
                {
                    // Videoyu C#'ta oynatmak sistemi çok kasar, o yüzden sadece şık bir bildirim bırakıyoruz
                    stack.Children.Add(new Label { Text = "🎥 [Video Mesajı - İzlemek için tarayıcıyı kullanın]", TextColor = Color.FromArgb("#0af2ff"), FontSize = 13, FontAttributes = FontAttributes.Bold });
                }
                // 3. EĞER GELEN MESAJ ROL KARTIYSA:
                else if (mesaj.Contains("role-card"))
                {
                    // Rol kartındaki o çirkin HTML taglarını silip temiz bir rozet bırakıyoruz
                    stack.Children.Add(new Label { Text = "🃏 [Gizli Rol Kartı Paylaşıldı]", TextColor = Color.FromArgb("#0af2ff"), FontSize = 13, FontAttributes = FontAttributes.Bold });
                }
                // 4. NORMAL YAZILI MESAJSA:
                else
                {
                    stack.Children.Add(new Label { Text = mesaj, TextColor = Colors.White, FontSize = 13 });
                }
            }
            catch
            {
                // Olur da Base64'ü çözerken bir hata çıkarsa program çökmesin diye güvenlik sigortası
                stack.Children.Add(new Label { Text = "⚠️ [Çözümlenemeyen İstihbarat Verisi]", TextColor = Colors.Red, FontSize = 13 });
            }

            border.Content = stack;
            SlSohbetGecmisi.Children.Add(border);
            // ==========================================
            // 🔥 OTOMATİK EN ALTA KAYDIRMA (AUTO-SCROLL) MOTORU 🔥
            // ==========================================
            // Ekranın mesaj balonunun boyunu hesaplaması için 50 milisaniye (salise) avans veriyoruz,
            // sonra şık bir animasyonla (true) en alta, yeni eklenen border'a kaydırıyoruz.
            Task.Run(async () =>
            {
                await Task.Delay(50);
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await SvSohbetGecmisi.ScrollToAsync(border, ScrollToPosition.End, true);
                });
            });
        }


        private void GercekKisiEkle(string peerID, string isim, string durumRengi)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                Padding = new Thickness(15, 12),
                BackgroundColor = Colors.Transparent
            };

            var stack = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
            stack.Children.Add(new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Color.FromArgb(durumRengi), VerticalOptions = LayoutOptions.Center });
            stack.Children.Add(new Label { Text = isim, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 13, VerticalOptions = LayoutOptions.Center });

            grid.Children.Add(stack);
            Grid.SetColumn(stack, 1);

            // 🔥 TIKLAMA (CLICK) MOTORU: Kişiye Tıklanınca Sohbeti Yükle 🔥
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) =>
            {
                // Diğerlerinin seçim rengini temizle, buna seçili rengi (gri) ver
                foreach (Grid child in SlKisiler.Children.OfType<Grid>())
                    child.BackgroundColor = Colors.Transparent;
                grid.BackgroundColor = Color.FromArgb("#37373D");

                // Üst barı güncelle
                _aktifBorussengerHedef = peerID;
                _aktifBorussengerHedefIsim = isim; // 🔥 YENİ EKLENEN 🔥
                LblSohbetHedef.Text = isim;

                // Sohbet Geçmişini Temizle ve Hafızadan Dök!
                SlSohbetGecmisi.Children.Clear();
                if (_csharpSohbetHafizasi.ContainsKey(peerID) && _csharpSohbetHafizasi[peerID].Count > 0)
                {
                    foreach (var msg in _csharpSohbetHafizasi[peerID])
                    {
                        bool bendenMi = msg.type == "me";
                        SahteMesajEkle(msg.sender, msg.msg, bendenMi); // Ekrana mesaj balonunu çiz
                    }
                }
                else
                {
                    SlSohbetGecmisi.Children.Add(new Label { Text = "Sohbet geçmişi yok...", TextColor = Colors.Gray, FontAttributes = FontAttributes.Italic, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 20, 0, 0) });
                }
            };
            grid.GestureRecognizers.Add(tapGesture);

            SlKisiler.Children.Add(grid);
        }


        // ==========================================
        // 🔥 BÖRÜSSENGER CANLI MESAJ & ROL FIRLATMA MOTOLARI 🔥
        // ==========================================

        private void OnBorussengerCanliMesajGonder(object sender, EventArgs e)
        {
            string mesaj = EntBorussengerCanliMesaj.Text;

            // Boş mesaja veya seçili adam yoksa işlem yapma
            if (string.IsNullOrWhiteSpace(mesaj) || string.IsNullOrEmpty(_aktifBorussengerHedefIsim))
                return;

            // JS tarafındaki mevcut komutu kullanıyoruz!
            // Biz JS'e gönderiyoruz, JS mesajı karşıya atıp anında bize "Mesaj atıldı ekrana çiz" diye geri dönecek!
            TimAskerlerineEmirGonder(new
            {
                komut = "BORUSSENGER_MESAJGONDER",
                hedef = _aktifBorussengerHedefIsim,
                mesaj = mesaj
            });

            // Tetiği çektikten sonra kutuyu temizle ve tekrar içine odaklan
            EntBorussengerCanliMesaj.Text = "";
            EntBorussengerCanliMesaj.Focus();
        }

        private void OnBorussengerCanliRolGonder(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_aktifBorussengerHedefIsim))
                return;

            // JS'e "Bu adama rolümü fırlat" diyoruz.
            TimAskerlerineEmirGonder(new
            {
                komut = "BORUSSENGER_ROLGONDER",
                hedef = _aktifBorussengerHedefIsim
            });
        }
    }
}