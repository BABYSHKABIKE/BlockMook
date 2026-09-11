# Сторонние компоненты и происхождение

BlockMook добавляет собственный интерфейс и управление подключением. Сетевой движок, конфигурационные идеи и перечисленные ниже библиотеки созданы авторами исходных проектов. Связи или официального одобрения со стороны этих проектов не заявляется.

## zapret и сборка Flowseal

- Авторы: [bol-van](https://github.com/bol-van/zapret) и [Flowseal](https://github.com/Flowseal/zapret-discord-youtube).
- Источник семи файлов: [тег 1.10.2](https://github.com/Flowseal/zapret-discord-youtube/tree/1.10.2/bin).
- Файлы: `winws.exe`, `WinDivert.dll`, `WinDivert64.sys`, `cygwin1.dll`, `quic_initial_www_google_com.bin`, `tls_clienthello_www_google_com.bin`, `ACTIVE_DISCORD_UDP.bin`.
- Сетевые бинарники и образцы пакетов не изменены. Конфигурации подключения адаптированы в `src/Core.cs` для выбранных сервисов и управления из приложения.
- Контрольные суммы: [engine.lock](engine.lock). Загрузка при сборке сверяется с этим файлом.
- Уведомление MIT bol-van / Flowseal: [Flowseal-MIT.txt](distribution-licenses/Flowseal-MIT.txt).

## WinDivert

Автор — Basil Projects. Используется под LGPLv3; альтернативная лицензия GPLv2 приведена в том же уведомлении. Полный текст: [WinDivert-LGPL.txt](distribution-licenses/WinDivert-LGPL.txt).

Исходники с файлами сборки: [WinDivert v2.2.2](https://github.com/basil00/WinDivert/tree/v2.2.2). Архив включён в релиз как `third-party-source/WinDivert-2.2.2-source.zip`.

Драйвер взят из дистрибутива Flowseal; его цифровая подпись отличается от подписи оригинальных драйверов Basil. Исходники не воспроизводят чужую Authenticode-подпись. BlockMook не переподписывает и не модифицирует драйвер.

## Cygwin / Newlib

`cygwin1.dll` сообщает версию **3.4.10**. Условия: LGPLv3+ и исключение для связывания, а также уведомления отдельных частей Newlib.

- [Cygwin notice и linking exception](distribution-licenses/Cygwin-NOTICE.txt)
- [LGPLv3](distribution-licenses/Cygwin-LGPL3.txt), [GPLv3](distribution-licenses/Cygwin-GPL3.txt)
- [Newlib notices](distribution-licenses/Newlib-LICENSE.txt)
- [Полный пакет исходников Cygwin 3.4.10-1](https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1-src.tar.xz), включая Newlib и cygport-рецепт сборки.

Исходники включены в релиз как `third-party-source/cygwin-3.4.10-1-src.tar.xz`. `prepare-sources.ps1` проверяет их SHA-256.

## Модификации и лицензирование

MIT в корне относится к коду BlockMook. Сторонние компоненты сохраняют собственные лицензии и авторство. Дополнительных ограничений на отладку модификаций библиотек не вводится.

Для локальной сборки с изменённой библиотекой сохрани структуру `app/engine`, осознанно обнови её запись в `engine.lock` и выполни `build.ps1`. Контроль хешей служит проверке целостности, а не запрету модификаций.
