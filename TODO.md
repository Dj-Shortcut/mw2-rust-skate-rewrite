# TODO: Rust skate-mod

Doel: een skate-mod die aanvoelt als skate., op de eigen Rust-server, voor de eigenaar en zijn vrienden.
De eigenaar beslist wie wat doet ([docs/AUTONOMY.md](docs/AUTONOMY.md)).

**Wat telt als klaar:** het is op Shadow in het spel gereden, en de log toont het resultaat.
Een groene build of een compile is nog niet klaar. Geen nieuwe features tot de lijst "Nu" leeg is.

## Nu, in deze volgorde

1. **MEGA-halfpipe rijdbaar zonder rukken** ([#487](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/487)).
   - Zichtbaar rond en herkend: gereden op client 0.18.21 (PR #496).
   - Blijft staan als je wegrijdt: 0.18.23 (PR #498), gereden op 18 oktober.
   - Open: terugzettingen op de klim; acceptatie is een klim omhoog en fakie terug met 0 terugzettingen in skate.log.
2. **Gewone halfpipe rijdbaar** ([#494](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/494)).
   - De serverbotsing volgt het ronde profiel: PR #495, gemerged.
   - Open: live zetten en dezelfde klim rijden.
3. **Bouwplan zichtbaar in de hand en een echte ghost bij plaatsen**
   ([#469](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/469) punt 13, punten 6 en 11).
   - Oorzaak: deze Rust-build verbergt de namen van spelerleden, dus de client ziet het item in je hand niet.
   - Nu toont Rust zijn eigen rode bouwplanschim.
4. **Rest van [#469](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/469)**.
   - Punt 10: flip en grab sluiten elkaar uit. Nog niet getest, want daar is een controller voor nodig.
   - Punt 12: board-geluid.
5. **Skategevoel met de controller in het spel bewijzen.**
   - Pushen, carven, remmen, flicktricks en momentum op rampen en bij landen.
   - Vergelijk met skate.
6. **Installatie voor een vriend**: zonder ontwikkeltools, met hints op het scherm, en spelen met twee clients.

## Wie doet wat

- Client, builds en tests op Shadow: Claude.
- Server (ShortcutSkate-plugin): Codex.
- Review en merge: Codex of de eigenaar.

## Geparkeerd

- De zelfstandige MW2-rewrite (sinds 5 oktober) en de MW2-gunplay (geschrapt op 14 oktober).
  Er komt geen nieuw werk aan.

## Geschiedenis

De oude, volledige lijst met alle eerdere bronstappen staat in [docs/TODO-HISTORY.md](docs/TODO-HISTORY.md).
