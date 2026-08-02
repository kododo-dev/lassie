---
project: "Lassie"
context_type: greenfield
created: 2026-08-02
updated: 2026-08-02
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "zakres persony MVP"
      decision: "MVP dla jednej firmy (nas) z jednym produktem, wieloma klientami; multi-tenant dla innych firm to cel docelowy, nie MVP"
    - topic: "typ bólu"
      decision: "brakująca zdolność — brak scentralizowanego narzędzia do zarządzania licencjami"
    - topic: "insight"
      decision: "wdrożenia rozproszone i offline-tolerant; obecne narzędzie (Intellilock) wiąże licencję z fizyczną maszyną, co zawodzi w chmurze; potrzeba wykrywania nieautoryzowanego współdzielenia licencji"
    - topic: "rola głównego użytkownika"
      decision: "MVP: jeden administrator; zespół wieloosobowy to cel docelowy"
    - topic: "auth admina"
      decision: "login email + hasło, model płaski, jedna rola"
    - topic: "auth aplikacji klienckiej"
      decision: "API key przypisany do licencji"
    - topic: "przepływ MVP"
      decision: "7 kroków: login admina, utworzenie klienta, utworzenie licencji (moduły+limit), generowanie klucza API, ręczne przekazanie klucza przy wdrożeniu, okresowe zapytanie API przez aplikację kliencką, odczyt statusu/modułów/limitów"
    - topic: "budżet czasowy MVP"
      decision: "3 tygodnie pracy po godzinach, bez twardego deadline'u"
    - topic: "granica odpowiedzialności enforcement"
      decision: "Lassie dostarcza poprawne dane o licencji; egzekwowanie limitów/blokowanie modułów leży po stronie aplikacji klienckiej, nie Lassie"
    - topic: "koncept 'Klient' w modelu danych"
      decision: "Usunięty z MVP. Licencja jest jednostką podstawową z własną etykietą tekstową (płaska lista, bez grupowania). Grupowanie licencji w foldery to nice-to-have poza MVP."
    - topic: "historia zmian licencji"
      decision: "Edycja licencji zapisuje historię/audyt zmian, nie tylko nadpisuje bieżące wartości"
    - topic: "reaktywacja licencji"
      decision: "Dezaktywacja licencji jest odwracalna (stan tymczasowy)"
    - topic: "rotacja klucza API"
      decision: "Poza MVP — brak możliwości regeneracji klucza bez utworzenia nowej licencji"
    - topic: "identyfikator instalacji w API weryfikacji"
      decision: "Poza MVP — API na razie tylko odczytuje status, bez heartbeat/identyfikatora instalacji"
    - topic: "szczegółowy powód nieważności licencji w API"
      decision: "Poza MVP — API zwraca prosty status ważna/nieważna"
    - topic: "reset hasła administratora"
      decision: "Poza MVP — odzyskiwanie dostępu obsługiwane ręcznie"
    - topic: "wyszukiwanie/filtrowanie listy licencji"
      decision: "Poza MVP — prosta lista wystarczy na start"
    - topic: "co decyduje o ważności licencji"
      decision: "Status aktywna/dezaktywowana ORAZ opcjonalna data wygaśnięcia — dodano pole daty wygaśnięcia do FR-005/FR-006"
    - topic: "reguła domenowa (Business Logic)"
      decision: "Lassie określa uprawnienia (moduły, limity) instancji klienckiej na podstawie stanu jej licencji; egzekwowanie zostaje po stronie aplikacji klienckiej"
    - topic: "NFR"
      decision: "Poufność klucza API, rozróżnienie błędu sieciowego od niewaznej licencji, responsywność panelu (desktop + smartfon), wsparcie 2 najnowszych wersji przeglądarek, <500ms odpowiedzi API"
    - topic: "typ produktu"
      decision: "web-app (panel administracyjny + wbudowane API)"
    - topic: "skala"
      decision: "small — jeden administrator, garstka licencji na start"
    - topic: "deadline"
      decision: "brak twardego terminu, praca wyłącznie po godzinach"
    - topic: "non-goals"
      decision: "brak płatności, brak hardware-lockingu/offline crypto (odejście od Intellilock), brak self-service portalu, brak multi-tenant, brak folderów, brak zaawansowanych modeli licencjonowania, brak powiadomień, brak wykrywania współdzielenia licencji, brak rotacji klucza, brak resetu hasła, brak wyszukiwania, brak i18n/white-label, brak zaawansowanej telemetrii"
  frs_drafted: 9
  quality_check_status: accepted
---

# Shape Notes: Lassie

Seed idea (from `context/foundation/idea-notes.md`): usługa do zarządzania licencjami dla firm, które wdrażają swoje aplikacje u klientów. Aplikacja klienta okresowo łączy się z usługą Lassie, by sprawdzić status licencji, dostępne moduły, maksymalną liczbę użytkowników itp.

## Vision & Problem Statement

Firma dostarczająca własny produkt wdrażany u wielu klientów nie ma dziś scentralizowanego sposobu zarządzania licencjami tych wdrożeń — nadawanie dostępu, ograniczanie modułów i limitu użytkowników odbywa się ręcznie. Obecnie używane narzędzie (Intellilock) wiąże licencję z fizyczną maszyną, co zawodzi w środowiskach chmurowych, gdzie fizyczna maszyna pod wdrożeniem zmienia się w czasie.

Wdrożenia klientów są rozproszone i nie zawsze online, więc weryfikacja licencji musi tolerować okresową, a nie ciągłą łączność — a jednocześnie musi zabezpieczać przed nieautoryzowanym współdzieleniem tej samej licencji, bez polegania na twardym powiązaniu ze sprzętem.

## User & Persona

**Primary persona**: Administrator licencji po stronie firmy-dostawcy — pojedyncza osoba (rola admina) odpowiedzialna za nadawanie, edycję i dezaktywację licencji dla klientów, którym wdrożono produkt. Sięga po Lassie przy każdym nowym wdrożeniu (utworzenie licencji), przy zmianie warunków umowy z klientem (zmiana modułów/limitów) oraz gdy trzeba zweryfikować lub wyłączyć licencję.

> Forward (poza MVP): docelowo dostęp do zarządzania licencjami ma mieć cały zespół (wdrożeniowy/support/sprzedaż), a Lassie ma stać się usługą, z której korzystają też inne firmy softwarowe zarządzające licencjami własnych produktów — nie tylko wy sami.

## Access Control

Dwa odrębne rodzaje dostępu:

- **Panel administracyjny (człowiek)**: logowanie email + hasło. Model płaski — jedna rola Administratora z pełnymi uprawnieniami (tworzenie/edycja/dezaktywacja licencji, przegląd statusów). Bez rozróżnienia ról w MVP.
- **API weryfikacji licencji (maszyna-do-maszyny)**: aplikacja kliencka uwierzytelnia się kluczem API przypisanym do konkretnej licencji, żeby okresowo sprawdzić jej status, dostępne moduły i limity.

> Forward (poza MVP): rozróżnienie ról w panelu (np. read-only dla supportu) dla wieloosobowego zespołu.

## Success Criteria

### Primary
- Administrator tworzy licencję w mniej niż 2 minuty przez panel
- Aplikacja kliencka poprawnie odczytuje status licencji, dostępne moduły i limity przez API

### Secondary
(brak — nie zidentyfikowano dodatkowego kryterium "mile widzianego")

### Guardrails
- Weryfikacja licencji przez API odpowiada w czasie poniżej 500ms
- API zawsze zwraca poprawny i aktualny status licencji (moduły, limity) — Lassie odpowiada za dokładność danych, nie za ich egzekwowanie po stronie aplikacji klienckiej

## Functional Requirements

### Klienci — koncept usunięty
- ~~FR-001: Administrator can tworzyć rekord klienta (nazwa, dane).~~ Usunięte.
- ~~FR-002: Administrator can edytować rekord klienta.~~ Usunięte.
- ~~FR-003: Administrator can dezaktywować klienta.~~ Usunięte.
  > Socrates: Podczas dyskusji nad FR-005 (kardynalność klient↔licencja) admin zakwestionował sens osobnego bytu "Klient". Rozwiązanie: koncept klienta usunięty z MVP — licencja jest jednostką podstawową, płaska lista bez grupowania. Grupowanie licencji w foldery to nice-to-have poza MVP (patrz `## Non-Goals` / Forward notes).

### Definicje modułów
- FR-004: Administrator can definiować i zarządzać listą dostępnych modułów licencyjnych (tworzenie/edycja definicji modułu). Priority: must-have
  > Socrates: Kontrargument rozważony: moduły mogłyby być hardcoded w kodzie zamiast zarządzane dynamicznie przez panel, żeby uprościć MVP. Rozwiązanie: zostaje must-have — produkt szybko się rozwija, dynamiczne zarządzanie modułami jest ważniejsze niż się wydaje.

### Licencje
- FR-005: Administrator can tworzyć licencję — nadając jej etykietę tekstową (np. nazwę klienta/wdrożenia), wybierając moduły spośród zdefiniowanych, limit użytkowników oraz opcjonalną datę wygaśnięcia. Priority: must-have
  > Socrates: Kontrargument rozważony: model "1 klient = 1 licencja" może być zbyt uproszczony (klient może potrzebować wielu licencji, np. test/prod). Rozwiązanie: koncept klienta usunięty — licencja jest samodzielną jednostką z własną etykietą tekstową, żadnej sztywnej kardynalności do egzekwowania.
- FR-006: Administrator can edytować licencję (moduły, limity, data wygaśnięcia), z zachowaniem historii poprzednich wersji do audytu. Priority: must-have
  > Socrates: Kontrargument rozważony: edycja mogłaby tylko nadpisywać bieżące wartości bez historii. Rozwiązanie: potrzebna historia zmian/audyt — każda edycja zapisywana jako wpis w historii.
- FR-007: Administrator can dezaktywować licencję, z możliwością późniejszej reaktywacji. Priority: must-have
  > Socrates: Kontrargument rozważony: dezaktywacja mogłaby być trwała (bez możliwości cofnięcia). Rozwiązanie: dezaktywacja to stan tymczasowy, reaktywacja możliwa.
- FR-008: System generuje unikalny (w całym systemie) klucz API dla licencji. Priority: must-have
  > Socrates: Kontrargument rozważony: brak możliwości rotacji/regeneracji klucza w razie wycieku. Rozwiązanie: brak kontrargumentu, rotacja klucza to nice-to-have poza MVP.

### API weryfikacji
- FR-009: Aplikacja kliencka can odpytać status licencji przez API, uwierzytelniając się kluczem API. Priority: must-have
  > Socrates: Kontrargument rozważony: zapytanie API mogłoby już teraz przesyłać identyfikator instalacji/heartbeat jako fundament pod przyszłe wykrywanie współdzielenia licencji. Rozwiązanie: poza MVP — API na razie tylko odczytuje status.
- FR-010: API zwraca ważność licencji, dostępne moduły i limit użytkowników. Priority: must-have
  > Socrates: Kontrargument rozważony: API mogłoby zwracać szczegółową przyczynę nieważności (wygasła vs dezaktywowana) zamiast prostego statusu. Rozwiązanie: prosty status wystarczy w MVP.

### Panel administracyjny
- FR-011: Administrator can zalogować się (email + hasło). Priority: must-have
  > Socrates: Kontrargument rozważony: reset hasła mógłby być zbędny, skoro jest tylko jeden administrator. Rozwiązanie: reset hasła zostaje poza MVP — w razie problemu dostęp odzyskiwany ręcznie (np. bezpośrednio w bazie danych).
- FR-012: Administrator can przeglądać listę licencji i ich bieżący status. Priority: must-have
  > Socrates: Kontrargument rozważony: lista mogłaby potrzebować wyszukiwania/filtrowania już w MVP. Rozwiązanie: prosta lista wystarczy — filtrowanie to nice-to-have poza MVP.

## User Stories

### US-01: Administrator tworzy licencję, aplikacja kliencka ją weryfikuje

- **Given** zalogowany Administrator
- **When** Administrator tworzy nową licencję, nadając jej etykietę, wybierając moduły i limit użytkowników
- **Then** system generuje unikalny klucz API; aplikacja kliencka używająca tego klucza otrzymuje z API poprawny status licencji, listę modułów i limit

#### Acceptance Criteria
- Klucz API jest unikalny w całym systemie
- Zapytanie API z niepoprawnym/brakującym kluczem zwraca błąd autoryzacji
- Odpowiedź API zawiera: ważność licencji, listę modułów, limit użytkowników
- Czas odpowiedzi API < 500ms

## Business Logic

Lassie określa, czy dana instancja aplikacji klienckiej ma prawo działać z określonymi modułami i limitami, na podstawie aktualnego stanu jej licencji (status aktywna/dezaktywowana oraz data wygaśnięcia).

Wejściem reguły jest klucz API identyfikujący licencję, przesyłany przez aplikację kliencką przy każdym zapytaniu. Wyjściem jest odpowiedź zawierająca ważność licencji (aktywna i niewygasła / nieaktywna lub wygasła), listę dostępnych modułów oraz limit użytkowników. Aplikacja kliencka odpytuje ten stan okresowo — nie w czasie rzeczywistym — i sama lokalnie stosuje otrzymane ograniczenia; decyzja o wpuszczeniu lub zablokowaniu konkretnego użytkownika zapada po stronie aplikacji klienckiej, na podstawie danych zwróconych przez Lassie.

## Non-Functional Requirements

- Klucz API nie jest nigdy ujawniany w postaci jawnej po pierwszym wygenerowaniu (ani w panelu, ani w logach dostępnych operatorowi)
- API rozróżnia jednoznacznie błąd sieciowy/niedostępność usługi od odpowiedzi "licencja nieważna" — chwilowa niedostępność Lassie nie jest równoznaczna z unieważnieniem licencji
- Panel administracyjny jest użyteczny i czytelny zarówno na ekranie desktopowym, jak i na małym ekranie (smartfon) — responsywny, bez utraty funkcjonalności
- Panel administracyjny jest użyteczny na dwóch najnowszych wersjach głównych przeglądarek
- Weryfikacja licencji przez API odpowiada w czasie poniżej 500ms (formalny NFR, powtórzony z Guardrails)

## Non-Goals

**Funkcjonalne:**
- Integracja z systemami płatności/fakturowania — zarządzane poza Lassie.
- Wiązanie licencji z fizycznym sprzętem lub szyfrowanie do pracy offline — świadome odejście od obecnie używanego narzędzia (Intellilock), które zawodzi w środowiskach chmurowych.
- Self-service portal dla klienta końcowego — w MVP licencjami zarządza wyłącznie administrator po stronie dostawcy.
- Obsługa wielu firm jako klientów Lassie (multi-tenant) — MVP służy tylko jednej firmie (nam); rozszerzenie na inne firmy to cel docelowy.
- Grupowanie licencji w foldery — licencja jest płaską, samodzielną jednostką w MVP.
- Zaawansowane modele licencjonowania (subskrypcje wielopoziomowe, trial z automatycznym wygasaniem, licencje floating/współdzielone między instancjami).
- Powiadomienia (e-mail/webhook) o zbliżającym się wygaśnięciu licencji.
- Wykrywanie nieautoryzowanego współdzielenia licencji (identyfikator instalacji, heartbeat) — cel docelowy, nie MVP.
- Rotacja/regeneracja klucza API bez tworzenia nowej licencji.
- Reset hasła / odzyskiwanie dostępu administratora.
- Wyszukiwanie/filtrowanie listy licencji.

**Niefunkcjonalne:**
- Wielojęzyczność i white-labeling panelu administracyjnego.
- Zaawansowana telemetria i analityka wykorzystania modułów przez klientów.
