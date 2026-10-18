# Doğrulama

Desteklenen yerel oyun dosyasıyla 18 Ekim 2026 tarihinde alınan sonuçlar. Bu kayıt farklı oyun sürümleri için uyumluluk garantisi değildir.

## Otomatik kontroller

- **49 motor kontrolü:** tür dönüşümleri, alan profili, geçersiz yazma istekleri, ayrı kontrollü süreçte bellek okuma/yazma ve geri alma, gerçek x64 atlama kodu ve bağlantı yaşam döngüsü.
- **4 statik oyun dosyası kontrolü:** SHA-256, tekil imza, yamalanan komutların içeriği ve PE bölüm eşlemesi. `PARANORMAL_GAME_EXE` tanımlandığında çalışır.
- **23 arayüz kontrolü:** 138 satır, 7 hızlı seçenek, salt okunur kontroller, Türkçe/İngilizce arama, kategoriler ve iki pencere boyutunda yerleşim.

Derleme/test komutları ana [README](../README.md) dosyasındadır. Oyun dosyası verilmeden toplam 72 kontrol, dosya ile toplam 76 kontrol çalışır. Arayüz testi oyuna bağlanmadan gerçek WPF kontrollerini PNG olarak çizer.

## Gerçek oyun testi

[Canlı test kaydı](verification/live-test-result.json), oyunun tek oyunculu bölümünde alınmıştır. 138 alanın okunması ve sınıf/ad/tür doğrulaması tamamlandı. Yedi hızlı seçenek sırayla etkinleştirilip kapatıldı:

- Can: `100 → 100 → 100`.
- Dayanıklılık: `97 → 100 → 97`.
- Koşu çarpanı: `1 → 2 → 1`.
- Zıplama: `300 → 900 → 300`.
- Canlandırma: `8 → 0,25 → 8`.
- Fener çarpanı: `1 → 3 → 1`.
- FOV: `90 → 110 → 90`.

Can örneği başlangıçta zaten azami değerdeydi; kayıtta görünür bir artış yoktur. Diğer örnekler bellek değişimini ve geri almayı gösterir. Test sonunda özgün 8 bayt oyun kodu geri yüklendi, oyun çalışmaya devam etti. Para/deneyim/kayıt alanlarına yazılmadı.

[Profil farkları](verification/profile-adjustments.json), özgün CT'ye göre 99 işaretçi zincirinin düzeltilmesini ve 138 alanın doğrulanmasını kaydeder.

## Sınırlar

Bu testler her seçeneğin bütün oyun mekaniklerine etkisini, fiziksel Num tuşlarıyla oyun içi kullanımı, bölüm geçişlerini veya uzun süreli oynanışı doğrulamaz. Can sabitlemenin ölümcül hasarı önlediği doğrulanmadı. Kısayolların Windows'a kaydı ve trainer penceresinin açılması kontrol edildi.

`tests/LiveEngineTest.cs` elle çalıştırılan, canlı oyunun belleğine yazan bir entegrasyon aracıdır. Normal `build.ps1 -Test` akışına dahil değildir. `LiveProbe.cs` ve `UeProbe.cs` salt okunur tanılama araçlarıdır. Canlı test sonucundaki zaman, otomatik testler yeniden çalıştırıldığında değişmez.
