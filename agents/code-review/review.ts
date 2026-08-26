import { query } from "@anthropic-ai/claude-agent-sdk";
import { REVIEW_SCHEMA, REVIEW_JSON_SCHEMA, SYSTEM_PROMPT, type Review } from "./review-schema.ts";

async function readDiff(): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of process.stdin) chunks.push(chunk as Buffer);
  return Buffer.concat(chunks).toString("utf8");
}

async function review(diff: string): Promise<Review> {
  const result = query({
    prompt: `Zrecenzuj ten diff:\n\n${diff}`,
    options: {
      systemPrompt: SYSTEM_PROMPT,
      model: "claude-sonnet-4-6",
      tools: [],
      maxTurns: 2,
      outputFormat: { type: "json_schema", schema: REVIEW_JSON_SCHEMA },
    },
  });

  for await (const message of result) {
    if (message.type !== "result") continue;
    if (message.subtype === "success") {
      const parsed = REVIEW_SCHEMA.safeParse(message.structured_output);
      if (!parsed.success) throw new Error(`Niepoprawny structured output: ${parsed.error.message}`);
      return parsed.data;
    }
    throw new Error(`Review nie powiodło się (${message.subtype}): ${message.errors.join("; ")}`);
  }
  throw new Error("Agent nie zwrócił wyniku");
}

const diff = await readDiff();
console.log(JSON.stringify(await review(diff), null, 2));
