КУДА КЛАСТЬ СВОЮ ИКОНКУ
========================

Положите сюда файл с любым из этих имен (любой из них достаточно):

    icon.png     <-- самый удобный вариант, желательно квадратный 512x512 или 1024x1024
    icon.ico     <-- если уже готовый мультиразмерный .ico
    icon.svg     <-- векторный, я сам отрендерю

Папка:  C:\Users\Naumov\Documents\v2crackN\_icon\

После этого скажите мне "иконка готова" — я:
  1) приведу к квадрату и наложу рамку/скругления при необходимости;
  2) разложу на размеры 16/24/32/48/64/128/256 и упакую в .ico;
  3) перезалью во все нужные места (см. ниже);
  4) пересоберу решение и проверю, что она видна и в файле .exe, и в трее.


ГДЕ ЛЕЖАТ ТЕКУЩИЕ ИКОНКИ (менять руками не нужно — всё делает tools/IconGen)
----------------------------------------------------------------------------
  v2rayN\v2rayN\Resources\v2rayN.ico              - иконка приложения (WPF)
  v2rayN\v2rayN\Resources\NotifyIcon1.ico          - трей: системный прокси ОЧИЩЕН (серая)
  v2rayN\v2rayN\Resources\NotifyIcon2.ico          - трей: прокси ПРИНУДИТЕЛЬНЫЙ (зелёная)
  v2rayN\v2rayN\Resources\NotifyIcon3.ico          - трей: прокси НЕ ТРОГАЕМ (синяя)
  v2rayN\v2rayN\Resources\NotifyIcon4.ico          - трей: PAC (жёлтая)
  v2rayN\v2rayN.Desktop\Assets\*.ico               - те же файлы для Avalonia-версии
  v2rayN\v2rayN.Desktop\v2rayN.png                 - PNG для упаковочных скриптов
  v2rayN\v2rayN.Desktop\v2rayN.icns                - macOS

СГЕНЕРИРОВАТЬ ЗАНОВО
--------------------
  cd C:\Users\Naumov\Documents\v2crackN
  dotnet run --project tools\IconGen -c Release

  Цвета градиентов задаются в tools\IconGen\Program.cs, массив icons[].
  preview_256.png / preview_64.png / preview_16.png здесь же — чтобы посмотреть.
