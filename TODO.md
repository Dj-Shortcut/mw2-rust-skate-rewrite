# TODO

**Doel:** skaten in Rust dat goed voelt, voor Andy en zijn vrienden, op zijn eigen server.

**Regel:** iets is pas *klaar* als Andy het in het spel heeft gereden of gezien. Gemerged, CI groen of "N checks geslaagd" telt niet als klaar.
Elke regel hieronder heeft één issue, één eigenaar en één zin die zegt wanneer het klaar is.
Nieuwe wensen van Andy komen eerst hier en in #469 te staan, met zijn eigen woorden, voordat iemand eraan begint.

Oude lijst (alles tot 18 oktober): [docs/TODO-HISTORY.md](docs/TODO-HISTORY.md).

## Eigenaarfeedback 20 oktober: lichaam, push en air

Andy over de .31-rit: "ik werd niet meer gerukt , en ik ben boven de coping geraakt, maar tijdens de air trekt mijn playermodel horizontaal recht".
Vervolg: "de rigging van het playermodel is nog altijd niet skate, elke truck is nog vreemd, pushen is nog raar" en "er ontbreken veel truuken".
Het goedgekeurde vervolg is eerst één samenhangende push → afzet → air → catch → landing, daarna meer tricks.

| Wat | Issue | Eigenaar | Klaar als |
|---|---|---|---|
| Board en lichaam delen dezelfde actuele pose; vloeiende push en air zonder ongevraagd rechtzetten | #469 | Codex, `codex/rider-air-frame-036` | Andy ziet en rijdt normale/mongo-push, een ollie/flip, transition-air en landing met samenhangende voet-/boardcontacten; de .31-rit zonder put-backs blijft behouden |

Status: client .35 is native gebouwd, met geverifieerde clientback-up geïnstalleerd en geladen. Een korte rit met toetsenbord bevestigde opstappen en beweging op de bestaande MEGA. Dat bewijst nog geen correcte controllerbesturing, airpose of hand-/voetcontacten. De eerdere feedback over controllerproblemen na afstappen en terugzetten tijdens het rijden blijft open.

De .35-presentatie koppelt rotatie, catch en grab-loslaten aan één vluchtstatus voor lichaam en board. De bronproeven dekken een doorlopende dubbele flip zonder tussentijdse catch/herhaalde flick, grab-loslaten tijdens een bodyspin en reset/herinstap. Deze contacten zijn nog niet visueel in het spel goedgekeurd.

De nieuwe .36-bron laat het getekende lichaam en board de vertrekhelling geleidelijk verlaten en de waargenomen landingshelling naderen. Een gemiste grondmeting wist die naderingsstand niet; een hoeklimiet voorkomt een plotselinge wissel. De echte gedeelde bron en actuele Rust/Unity-API zijn gecontroleerd. De fysieke beweging, netwerk-handoff en bediening blijven ongewijzigd. Native bouw, installatie en visuele rijvalidatie van .36 staan nog open. Bij een laat waargenomen steile tegenwand kan de houding door de hoeklimiet nog achterlopen op de landingshelling. Klaar pas wanneer Andy de volledige afzet → air → catch → landing samenhangend ziet en rijdt.

De geregistreerde MEGA is na de toegestane serverherstart met zijn 80 delen en gladde botsingsvlakken gecontroleerd. De oude, ongeregistreerde MEGA werd niet hersteld: de 80 oude entiteiten ontbraken. Dat is geen bewijs voor een foutloze rit op de huidige parkstukken.

## Eigenaarfeedback 21 oktober: pompen is geen voetpush

Andy: "door de knieen buigen om snelheid te maken, niet afpushen met een voet".

Pompen betekent op het board inveren en getimed uitstrekken in de transition. Beide voeten blijven op het board; sturen of de triggers loslaten mag geen ongevraagde voetpush starten. De .34-bron onderbreekt een voetstroke bij controllercrouch en wacht bij een aangehouden voorwaartse stick op terugkeer voordat die opnieuw pusht. Klaar wanneer Andy de kniebuiging en het uitstrekken ziet, daardoor voelbaar snelheid opbouwt en boven de coping komt zonder servercorrecties. Een push- of grabverbetering sluit dit punt niet.

## Nu: de halfpipe moet rijdbaar zijn

Niets anders gaat voor, tot dit klaar is.

| # | Wat | Issue / PR | Eigenaar | Klaar als |
|---|-----|------------|----------|-----------|
| 1 | Gewone halfpipe: server zet je niet terug op de wand | #494, PR #495 (gemerged; live nog niet bevestigd) | Codex | Andy rijdt 3× omhoog en fakie terug, `skate.log` toont 0 put-backs |
| 2 | Client: contact en momentum in de transitie tot de lip | #487, PR #496 (open) | Codex | Zelfde rit als 1, zonder schokken of stilstaan op de wand |
| 3 | MEGA-halfpipe: volledig zichtbaar en rijdbaar | #487, PR #496 | Codex | Andy ziet beide ronde zijden en rijdt er 3× op en af |

## Daarna: bouwen moet werken

| # | Wat | Issue / PR | Eigenaar | Klaar als |
|---|-----|------------|----------|-----------|
| 4 | Bouwplan zichtbaar in de hand: het Skate-stuk is te zien waar je mikt | #469 punt 13, PR #497 | Claude | Andy houdt een Skate-item vast en ziet een groene omtrek op een fundering |
| 5 | Alle 12 parkstukken plaatsen | #480 | Codex | Per stuk "geplaatst" of de exacte F1-weigertekst, alle 12 geplaatst |
| 6 | Stukken liggen op de grond/fundering, niet zwevend; snappen zoals Rust-bouwen | #469 punten 5–6 | Codex | Andy plaatst een kicker en een quarterpipe op funderingen, ze sluiten aan |

## Daarna: rijden zelf

| # | Wat | Issue / PR | Eigenaar | Klaar als |
|---|-----|------------|----------|-----------|
| 7 | Niets in de hand en niet bouwen tijdens het rijden | #469 punten 7 en 11; PR #519 en #521 gemerged, volledige rijvalidatie blijft open | Claude | Op het board: geen item zichtbaar, geen rode bouwpreview, niets te bouwen |
| 8 | Flip en grab niet tegelijk | #469 punt 10; PR #520 gemerged, volledige controller-/rijvalidatie blijft open | Claude | Met controller: grab vasthouden en flicken geeft geen flip+grab, en de grab loslaten flipt het board niet |
| 9 | Boardgeluid klinkt als echt skaten | #469 punt 12 | open | Andy vindt het geluid oké |
| 9b | Altijd dag op de server, zodat tests niet in het donker gebeuren | PR #502 | Claude | Overdag in het spel, en het blijft dag |

## Nodig om sneller te gaan

| # | Wat | Eigenaar | Klaar als |
|---|-----|----------|-----------|
| 10 | Agents kunnen zelf op Shadow testen (Tailscale SSH of OpenSSH, Shadow staat aan) | Andy beslist | Een agent installeert een client en leest `skate.log` zonder Andy |

## Bij elke speltest kijken we naar meer dan het ene punt

Niet alleen "werkt het nieuwe ding", maar elke keer ook (overdag, in third person én first person, met screenshots):

- **Rijder en board:** staat het lichaam recht op het board, ook op steile stukken en de vert? Zweeft of zakt het board door iets? Kloppen voeten, handen en hoofd?
- **Beweging:** schokken, terugzetten (put-backs in `skate.log`), haperen op naden, plots stilstaan of wegschieten?
- **Camera en zicht:** draait het kompas, staat het zicht scheef, kijkt de camera door muren of de grond?
- **Stukken:** liggen ze op de grond, sluiten ze aan, zijn alle delen zichtbaar (geen ontbrekende zijden of gaten)?
- **Hand en HUD:** wat zit er in de hand, staan er oude previews of meldingen in beeld?
- **Geluid:** klopt het geluid met wat je ziet?
- **Logs:** `skate.log` en de Oxide-log op fouten en waarschuwingen nakijken, niet alleen de ene verwachte regel.

Alles wat opvalt komt in het testverslag, ook als het niet bij de PR hoort.

## Bijhouden

- Eén PR per regel. In de PR staat: gereden of gezien ja/nee, en wat er gebeurde.
- Wordt een regel klaar, dan gaat hij hier weg en komt de datum en het bewijs (wie reed, wat te zien was) in het issue.
- Geen nieuwe functies tot rij 1–3 klaar zijn, tenzij Andy het vraagt.
