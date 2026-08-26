import { z } from "zod";

export const SYSTEM_PROMPT = `Jesteś precyzyjnym, konstruktywnym recenzentem kodu oceniającym pull request
w projekcie Lassie (ASP.NET Core / Blazor Server, C#). Oceń podany diff w pięciu kryteriach w skali 1-10
(1 = poważne braki, 10 = wzorowo): poprawność implementacji, idiomatyczność (konwencje C#/.NET),
złożoność, pokrycie testami względem ryzyka, bezpieczeństwo.
Zwróć szczególną uwagę na dwie zasady tego projektu: klucze API licencji nigdy nie mogą pojawić się
w plaintext (UI, logi, komentarze) po ich wygenerowaniu, a edycje licencji muszą zachowywać historię
audytową (bez destrukcyjnego nadpisania).
Następnie wydaj wiążący werdykt (pass/fail) dla całej zmiany i dołącz krótkie podsumowanie (2-3 zdania)
w Markdown, na podstawie którego autor PR-a będzie mógł działać.`;

// Score'y trzymamy jako zwykłe z.number(): structured output Anthropica odrzuca
// minimum/maximum na typie integer, więc zakres 1-10 wymuszamy opisem pola i promptem,
// a nie samym schematem.
export const REVIEW_SCHEMA = z.object({
  implementationCorrectness: z.number().describe("Poprawność implementacji: czy kod robi to, co deklaruje (skala 1-10)"),
  idiomaticity: z.number().describe("Idiomatyczność: zgodność z konwencjami C#/.NET i projektu (skala 1-10)"),
  complexity: z.number().describe("Złożoność: prostota rozwiązania względem problemu (skala 1-10)"),
  testRiskCoverage: z.number().describe("Pokrycie testami proporcjonalne do ryzyka zmienianych ścieżek (skala 1-10)"),
  securitySafety: z.number().describe("Bezpieczeństwo: brak podatności i wycieków sekretów, w tym plaintext API key (skala 1-10)"),
  verdict: z.enum(["pass", "fail"]).describe("Wiążący werdykt dla całej zmiany"),
  summary: z.string().describe("Podsumowanie w Markdown, gotowe jako komentarz do PR-a"),
});

export const REVIEW_JSON_SCHEMA = z.toJSONSchema(REVIEW_SCHEMA, { target: "draft-07" });

export type Review = z.infer<typeof REVIEW_SCHEMA>;
