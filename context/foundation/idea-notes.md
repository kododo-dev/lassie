## Lassie - MVP

### Główny problem
Firmy dostarczające aplikacje wdrażane u klientów nie mają prostego, scentralizowanego sposobu zarządzania licencjami tych wdrożeń. Nadawanie dostępu, ograniczanie modułów czy liczby użytkowników odbywa się ręcznie (pliki konfiguracyjne, maile, arkusze), co jest podatne na błędy, trudne do audytowania i praktycznie niemożliwe do wyegzekwowania, gdy klient przekroczy warunki umowy.

### Najmniejszy zestaw funkcjonalności
- Zarządzanie klientami i przypisanymi im licencjami (tworzenie, edycja, dezaktywacja)
- Definiowanie planów licencyjnych: dostępne moduły oraz limity (np. maksymalna liczba użytkowników)
- Generowanie unikalnego klucza/identyfikatora licencji dla instancji aplikacji u klienta
- API do weryfikacji statusu licencji przez aplikację kliencką (ważność, dostępne moduły, limity)
- Prosty panel administracyjny do przeglądu licencji i ich bieżącego statusu
- Uwierzytelnianie żądań API (API key przypisany do licencji)

### Co NIE wchodzi w zakres MVP
- Płatności i fakturowanie (integracja z systemami billingowymi)
- Zaawansowane modele licencjonowania (subskrypcje wielopoziomowe, trial z automatycznym wygasaniem, licencje floating/współdzielone między instancjami)
- Ochrona antypiracka / podpisywanie i szyfrowanie licencji do pracy offline
- Self-service portal, w którym klient końcowy sam zarządza swoją licencją
- Powiadomienia (e-mail/webhook) o zbliżającym się wygaśnięciu licencji
- Wielojęzyczność i white-labeling panelu administracyjnego
- Zaawansowana telemetria i analityka wykorzystania modułów przez klientów

### Kryteria sukcesu
- Aplikacja kliencka jest w stanie zweryfikować status licencji przez API w czasie poniżej 500ms
- 100% testowych licencji poprawnie blokuje dostęp do modułu lub sygnalizuje przekroczenie limitu użytkowników po stronie klienta
- Administrator jest w stanie utworzyć nową licencję i przypisać ją do klienta w mniej niż 2 minuty przez panel
