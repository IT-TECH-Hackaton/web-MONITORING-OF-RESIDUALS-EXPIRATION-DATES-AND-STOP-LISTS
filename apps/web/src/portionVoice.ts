export type VoiceDish = { dishId: string; dishName: string };

export type PortionProposal =
  | { ok: true; dishId: string; dishName: string; delta: number; heard: string; matchesOpen: boolean }
  | { ok: false; heard: string; reason: string };

const NUMBER_WORDS: Record<string, number> = {
  один: 1,
  одна: 1,
  одно: 1,
  два: 2,
  две: 2,
  три: 3,
  четыре: 4,
  пять: 5,
  шесть: 6,
  семь: 7,
  восемь: 8,
  девять: 9,
  десять: 10,
};

const PLUS = ["плюс", "+", "добав", "доготов", "ещё", "еще"];
const MINUS = ["минус", "-", "убав", "убери", "сними"];

function normalize(value: string) {
  return value
    .toLocaleLowerCase("ru")
    .replaceAll("ё", "е")
    .replace(/[+]/g, " + ")
    .replace(/[-−–]/g, " - ")
    .replace(/[^\p{L}\p{N}+\- ]/gu, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function tokens(value: string) {
  return normalize(value).split(" ").filter((token) => token.length >= 3);
}

function readNumber(words: string[], index: number) {
  const word = words[index];
  if (!word) return null;
  if (/^\d+$/.test(word)) {
    const value = Number(word);
    return value > 0 && value <= 30 ? value : null;
  }
  return NUMBER_WORDS[word] ?? null;
}

function readDelta(words: string[]) {
  for (let index = 0; index < words.length; index += 1) {
    const word = words[index];
    const plus = PLUS.some((cue) => word === cue || word.startsWith(cue));
    const minus = MINUS.some((cue) => word === cue || word.startsWith(cue));
    if (!plus && !minus) continue;
    const amount = readNumber(words, index + 1) ?? readNumber(words, index - 1);
    if (amount) return (minus ? -1 : 1) * amount;
  }
  return null;
}

function scoreDish(dishName: string, phrase: string) {
  const nameTokens = tokens(dishName);
  if (nameTokens.length === 0) return 0;
  const phraseTokens = tokens(phrase);
  let hits = 0;
  for (const name of nameTokens) {
    const stem = name.slice(0, Math.min(5, name.length));
    if (phraseTokens.some((token) => token.startsWith(stem) || name.startsWith(token.slice(0, Math.min(5, token.length))))) {
      hits += 1;
    }
  }
  return hits / nameTokens.length;
}

/** Фраза вроде «паста плюс 3» становится блюдом и дельтой. На склад это не похоже и само ничего не меняет. */
export function parsePortionPhrase(heard: string, dishes: VoiceDish[], openDishId: string): PortionProposal {
  const phrase = normalize(heard);
  if (!phrase) {
    return { ok: false, heard, reason: "Пустая фраза. Ничего не изменил." };
  }

  const delta = readDelta(phrase.split(" "));
  if (delta === null) {
    return { ok: false, heard, reason: "Не услышал число порций. Скажите «плюс 3» или «минус 1»." };
  }

  const filler = (word: string) =>
    PLUS.some((cue) => word === cue || word.startsWith(cue))
    || MINUS.some((cue) => word === cue || word.startsWith(cue))
    || Boolean(NUMBER_WORDS[word])
    || /^\d+$/.test(word)
    || word.length < 3
    || word.startsWith("порц")
    || word.startsWith("заготов");

  const ranked = dishes
    .map((dish) => ({ dish, score: scoreDish(dish.dishName, phrase) }))
    .filter((item) => item.score > 0)
    .sort((a, b) => b.score - a.score || a.dish.dishName.localeCompare(b.dish.dishName, "ru"));

  const open = dishes.find((dish) => dish.dishId === openDishId);
  const named = ranked.filter((item) => item.score >= 0.34);
  let chosen: VoiceDish | undefined = named[0]?.dish;
  if (named.length > 1 && named[0].score === named[1].score) {
    return { ok: false, heard, reason: "Подошли два блюда. Назовите одно и повторите. Ничего не изменил." };
  }
  if (!chosen) {
    const residue = phrase.split(" ").filter((word) => !filler(word));
    if (residue.length > 0) {
      return { ok: false, heard, reason: "Не узнал блюдо на смене. Ничего не изменил." };
    }
    chosen = open;
  }
  if (!chosen) {
    return { ok: false, heard, reason: "Не узнал блюдо. Ничего не изменил." };
  }

  return {
    ok: true,
    dishId: chosen.dishId,
    dishName: chosen.dishName,
    delta,
    heard,
    matchesOpen: chosen.dishId === openDishId,
  };
}
