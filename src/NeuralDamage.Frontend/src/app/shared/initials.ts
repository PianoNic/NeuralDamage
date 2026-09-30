/** Two letters for an avatar fallback: "Rex" → "Re", "Ada Lovelace" → "AL". */
export function initials(name: string | null | undefined): string {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) {
    const [word] = words;
    return word.charAt(0).toLocaleUpperCase() + word.charAt(1).toLocaleLowerCase();
  }
  return (words[0].charAt(0) + words[1].charAt(0)).toLocaleUpperCase();
}
