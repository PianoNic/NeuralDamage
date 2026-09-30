import { ReplyInfoDto } from '../../core/models';

/** Ends in . ! ? or an ellipsis, maybe followed by closing quotes or brackets. */
const TERMINAL = /(?:[.!?]|…)["'”’)\]]*$/;

/** The text as one sentence: whitespace collapsed, and a full stop only when it has no ending. */
export function asSentence(text: string): string {
  const flat = text.replace(/\s+/g, ' ').trim();
  if (!flat) return '';
  return TERMINAL.test(flat) ? flat : `${flat}.`;
}

/**
 * What a screen reader hears for the one-line reply reference: "Replying to Alice: are you there?
 * Go to that message." A question keeps its question mark instead of turning into "?.".
 */
export function replyLabel(reply: Pick<ReplyInfoDto, 'senderName' | 'content'>): string {
  const quoted = asSentence(reply.content) || 'An image.';
  return `Replying to ${reply.senderName}: ${quoted} Go to that message.`;
}
