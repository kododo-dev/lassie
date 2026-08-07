---
project: "Lassie"
version: 1
status: draft
created: 2026-08-02
updated: 2026-08-07
context_type: greenfield
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

# PRD: Lassie

## Vision & Problem Statement

Firma dostarczająca własny produkt wdrażany u wielu klientów nie ma dziś scentralizowanego sposobu zarządzania licencjami tych wdrożeń — nadawanie dostępu i konfigurowanie parametrów licencji (np. rodzaju, limitu użytkowników) odbywa się ręcznie. Obecnie używane narzędzie (Intellilock) wiąże licencję z fizyczną maszyną, co zawodzi w środowiskach chmurowych, gdzie fizyczna maszyna pod wdrożeniem zmienia się w czasie.

Wdrożenia klientów są rozproszone i nie zawsze online, więc weryfikacja licencji musi tolerować okresową, a nie ciągłą łączność — a jednocześnie musi zabezpieczać przed nieautoryzowanym współdzieleniem tej samej licencji, bez polegania na twardym powiązaniu ze sprzętem.

## User & Persona

**Primary persona**: Administrator licencji po stronie firmy-dostawcy — pojedyncza osoba (rola admina) odpowiedzialna za nadawanie, edycję i dezaktywację licencji dla klientów, którym wdrożono produkt. Sięga po Lassie przy każdym nowym wdrożeniu (utworzenie licencji), przy zmianie warunków umowy z klientem (edycja licencji) oraz gdy trzeba zweryfikować lub wyłączyć licencję.

> Forward (poza MVP): docelowo dostęp do zarządzania licencjami ma mieć cały zespół (wdrożeniowy/support/sprzedaż), a Lassie ma stać się usługą, z której korzystają też inne firmy softwarowe zarządzające licencjami własnych produktów — nie tylko wy sami.

## Success Criteria

### Primary
- Administrator tworzy licencję w mniej niż 2 minuty przez panel
- Aplikacja kliencka poprawnie odczytuje status licencji przez API

### Secondary
(brak — nie zidentyfikowano dodatkowego kryterium "mile widzianego")

### Guardrails
- Weryfikacja licencji przez API odpowiada w czasie poniżej 500ms
- API zawsze zwraca poprawny i aktualny status licencji — Lassie odpowiada za dokładność danych, nie za ich egzekwowanie po stronie aplikacji klienckiej

## User Stories

### US-01: Administrator tworzy licencję, aplikacja kliencka ją weryfikuje

- **Given** zalogowany Administrator
- **When** Administrator tworzy nową licencję, nadając jej etykietę oraz opcjonalną datę wygaśnięcia
- **Then** system generuje unikalny klucz API; aplikacja kliencka używająca tego klucza otrzymuje z API poprawny status licencji

#### Acceptance Criteria
- Klucz API jest unikalny w całym systemie
- Zapytanie API z niepoprawnym/brakującym kluczem zwraca błąd autoryzacji
- Odpowiedź API zawiera ważność licencji
- Czas odpowiedzi API < 500ms

## Functional Requirements

### Klienci — koncept usunięty
- ~~FR-001: Administrator can tworzyć rekord klienta (nazwa, dane).~~ Usunięte.
- ~~FR-002: Administrator can edytować rekord klienta.~~ Usunięte.
- ~~FR-003: Administrator can dezaktywować klienta.~~ Usunięte.
  > Socrates: Podczas dyskusji nad FR-005 (kardynalność klient↔licencja) admin zakwestionował sens osobnego bytu "Klient". Rozwiązanie: koncept klienta usunięty z MVP — licencja jest jednostką podstawową, płaska lista bez grupowania. Grupowanie licencji w foldery to nice-to-have poza MVP (patrz `## Non-Goals`).

### Definiowalne pola licencji — odroczone poza MVP
- ~~FR-004: Administrator can definiować i zarządzać zestawem pól opisujących licencję — nazwa pola oraz typ danych (liczba / tekst / wybór jednokrotny z listy), a dla pól typu wyboru: zarządzać listą dozwolonych opcji.~~ Odroczone poza MVP jako nice-to-have (patrz `## Non-Goals`) — MVP licencji ogranicza się do nazwy i daty wygaśnięcia (FR-005).
  > Socrates: Kontrargument rozważony: zestaw pól mógłby być stały/hardcoded (zawsze "rodzaj" + "limit użytkowników") zamiast w pełni konfigurowalny, żeby uprościć MVP. Rozwiązanie (2026-08-06): zostaje w pełni konfigurowalny.
  > Zmiana (2026-08-07): po zaimplementowaniu p1-p3 (`context/changes/module-catalog-management/`) admin ocenił, że w pełni konfigurowalny schemat pól to zbędny narzut na starcie — dodaje realny zakres (dynamiczny formularz tworzenia licencji, dynamiczne przechowywanie wartości) bez którego można udowodnić kluczową hipotezę produktu. Kod cofnięty (`change.md` w tym samym katalogu ma szczegóły rewertu), FR-004 odroczone jako nice-to-have po MVP zamiast usunięte na stałe.

### Licencje
- FR-005: Administrator can tworzyć licencję — nadając jej etykietę tekstową (np. nazwę klienta/wdrożenia) oraz opcjonalną datę wygaśnięcia. Priority: must-have
  > Socrates: Kontrargument rozważony: model "1 klient = 1 licencja" może być zbyt uproszczony (klient może potrzebować wielu licencji, np. test/prod). Rozwiązanie: koncept klienta usunięty — licencja jest samodzielną jednostką z własną etykietą tekstową, żadnej sztywnej kardynalności do egzekwowania.
- FR-006: Administrator can edytować licencję (nazwa, data wygaśnięcia), z zachowaniem historii poprzednich wersji do audytu. Priority: must-have
  > Socrates: Kontrargument rozważony: edycja mogłaby tylko nadpisywać bieżące wartości bez historii. Rozwiązanie: potrzebna historia zmian/audyt — każda edycja zapisywana jako wpis w historii.
- FR-007: Administrator can dezaktywować licencję, z możliwością późniejszej reaktywacji. Priority: must-have
  > Socrates: Kontrargument rozważony: dezaktywacja mogłaby być trwała (bez możliwości cofnięcia). Rozwiązanie: dezaktywacja to stan tymczasowy, reaktywacja możliwa.
- FR-008: System generuje unikalny (w całym systemie) klucz API dla licencji. Priority: must-have
  > Socrates: Kontrargument rozważony: brak możliwości rotacji/regeneracji klucza w razie wycieku. Rozwiązanie: brak kontrargumentu, rotacja klucza to nice-to-have poza MVP.

### API weryfikacji
- FR-009: Aplikacja kliencka can odpytać status licencji przez API, uwierzytelniając się kluczem API. Priority: must-have
  > Socrates: Kontrargument rozważony: zapytanie API mogłoby już teraz przesyłać identyfikator instalacji/heartbeat jako fundament pod przyszłe wykrywanie współdzielenia licencji. Rozwiązanie: poza MVP — API na razie tylko odczytuje status.
- FR-010: API zwraca ważność licencji. Priority: must-have
  > Socrates: Kontrargument rozważony: API mogłoby zwracać szczegółową przyczynę nieważności (wygasła vs dezaktywowana) zamiast prostego statusu. Rozwiązanie: prosty status wystarczy w MVP.
  > Zmiana (2026-08-07): pierwotnie odpowiedź miała zawierać też wartości definiowalnych pól (FR-004) — odroczone poza MVP razem z FR-004, patrz ten wpis.

### Panel administracyjny
- FR-011: Administrator can zalogować się (email + hasło). Priority: must-have
  > Socrates: Kontrargument rozważony: reset hasła mógłby być zbędny, skoro jest tylko jeden administrator. Rozwiązanie: reset hasła zostaje poza MVP — w razie problemu dostęp odzyskiwany ręcznie.
- FR-012: Administrator can przeglądać listę licencji i ich bieżący status. Priority: must-have
  > Socrates: Kontrargument rozważony: lista mogłaby potrzebować wyszukiwania/filtrowania już w MVP. Rozwiązanie: prosta lista wystarczy — filtrowanie to nice-to-have poza MVP.

## Non-Functional Requirements

- Klucz API nie jest nigdy ujawniany w postaci jawnej po pierwszym wygenerowaniu (ani w panelu, ani w logach dostępnych operatorowi)
- API rozróżnia jednoznacznie błąd sieciowy/niedostępność usługi od odpowiedzi "licencja nieważna" — chwilowa niedostępność Lassie nie jest równoznaczna z unieważnieniem licencji
- Panel administracyjny jest użyteczny i czytelny zarówno na ekranie desktopowym, jak i na małym ekranie (smartfon) — responsywny, bez utraty funkcjonalności
- Panel administracyjny jest użyteczny na dwóch najnowszych wersjach głównych przeglądarek
- Weryfikacja licencji przez API odpowiada w czasie poniżej 500ms

## Business Logic

Lassie określa ważność licencji na podstawie aktualnego stanu licencji (status aktywna/dezaktywowana oraz data wygaśnięcia). Interpretacja, co ważność licencji oznacza dla dostępnych funkcji, leży po stronie aplikacji klienckiej.

Wejściem reguły jest klucz API identyfikujący licencję, przesyłany przez aplikację kliencką przy każdym zapytaniu. Wyjściem jest odpowiedź zawierająca ważność licencji (aktywna i niewygasła / nieaktywna lub wygasła). Aplikacja kliencka odpytuje ten stan okresowo — nie w czasie rzeczywistym — i sama stosuje otrzymane ograniczenia; decyzja o wpuszczeniu lub zablokowaniu konkretnego użytkownika zapada po stronie aplikacji klienckiej, na podstawie danych zwróconych przez Lassie.

> Forward (poza MVP): gdy definiowalne pola licencji (FR-004, odroczone jako nice-to-have) wrócą, ta reguła i odpowiedź API rozszerzą się o wartości pól — patrz FR-004, FR-010.

## Access Control

Dwa odrębne rodzaje dostępu:

- **Panel administracyjny (człowiek)**: logowanie email + hasło. Model płaski — jedna rola Administratora z pełnymi uprawnieniami (tworzenie/edycja/dezaktywacja licencji, przegląd statusów). Bez rozróżnienia ról w MVP.
- **API weryfikacji licencji (maszyna-do-maszyny)**: aplikacja kliencka uwierzytelnia się kluczem API przypisanym do konkretnej licencji, żeby okresowo sprawdzić jej status.

> Forward (poza MVP): rozróżnienie ról w panelu (np. read-only dla supportu) dla wieloosobowego zespołu.

## Non-Goals

**Funkcjonalne:**
- Integracja z systemami płatności/fakturowania — zarządzane poza Lassie.
- Wiązanie licencji z fizycznym sprzętem lub szyfrowanie do pracy offline — świadome odejście od obecnie używanego narzędzia (Intellilock), które zawodzi w środowiskach chmurowych.
- Self-service portal dla klienta końcowego — w MVP licencjami zarządza wyłącznie administrator po stronie dostawcy.
- Obsługa wielu firm jako klientów Lassie (multi-tenant) — MVP służy tylko jednej firmie (nam); rozszerzenie na inne firmy to cel docelowy.
- Grupowanie licencji w foldery — licencja jest płaską, samodzielną jednostką w MVP.
- Zaawansowane modele licencjonowania (subskrypcje wielopoziomowe, trial z automatycznym wygasaniem, licencje floating/współdzielone między instancjami) — nawet gdy definiowalne pola licencji (FR-004) wrócą jako nice-to-have, mają być elastycznym, ale płaskim zestawem atrybutów opisowych bez wpływu na dostępne funkcje ani wbudowanej logiki subskrypcyjnej (billing, auto-wygasanie, floating), nie systemem tierowanych planów.
- Rozszerzanie zestawu dostępnych typów danych pola (poza liczba/tekst/wybór jednokrotny) bez zmiany kodu — gdy FR-004 wróci, typy danych mają pozostać zamkniętym, zakodowanym zestawem; dynamiczne mają być tylko nazwy i wystąpienia pól, nie same prymitywy typów.
- **Definiowalne, admin-konfigurowalne pola licencji (FR-004: nazwa pola + typ danych liczba/tekst/wybór, opcje dla pól wyboru)** — poza MVP jako nice-to-have. MVP licencji ogranicza się do etykiety tekstowej i daty wygaśnięcia (FR-005). Częściowa implementacja (`LicenseField`/`LicenseFieldOption`) była zbudowana i została cofnięta 2026-08-07 — patrz `context/changes/module-catalog-management/change.md`; `research.md`/`plan.md` w tym samym katalogu zostają jako gotowy punkt startowy, gdy ta praca wróci.
- Powiadomienia o zbliżającym się wygaśnięciu licencji.
- Wykrywanie nieautoryzowanego współdzielenia licencji — cel docelowy, nie MVP.
- Rotacja/regeneracja klucza API bez tworzenia nowej licencji.
- Reset hasła / odzyskiwanie dostępu administratora.
- Wyszukiwanie/filtrowanie listy licencji.

**Niefunkcjonalne:**
- Wielojęzyczność i white-labeling panelu administracyjnego.
- Zaawansowana telemetria i analityka wykorzystania licencji przez klientów.

## Open Questions

Brak nierozwiązanych kwestii. `shape-notes.md` przeszedł cross-check jakości ze statusem `accepted` (faza 7 zakończona bez zidentyfikowanych luk) — wszystkie wymagane sekcje PRD miały pełne pokrycie w danych wejściowych.
