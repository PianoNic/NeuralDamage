import { asSentence, replyLabel } from './reply-label';

describe('reply labels', () => {
  it('adds a full stop only when the quote has no ending of its own', () => {
    expect(replyLabel({ senderName: 'Alice', content: 'are you there?' })).toBe(
      'Replying to Alice: are you there? Go to that message.',
    );
    expect(replyLabel({ senderName: 'Alice', content: 'hello' })).toBe(
      'Replying to Alice: hello. Go to that message.',
    );
    expect(replyLabel({ senderName: 'Grok', content: 'wow!' })).toContain('wow! Go');
    expect(replyLabel({ senderName: 'Grok', content: 'fine.' })).toContain('fine. Go');
  });

  it('treats ellipses and closing quotes as endings', () => {
    expect(asSentence('well...')).toBe('well...');
    expect(asSentence('well…')).toBe('well…');
    expect(asSentence('she said "why?"')).toBe('she said "why?"');
    expect(asSentence('(really?)')).toBe('(really?)');
  });

  it('flattens line breaks and names an image-only message', () => {
    expect(asSentence('one\n\ntwo  ')).toBe('one two.');
    expect(replyLabel({ senderName: 'Bob', content: '' })).toBe(
      'Replying to Bob: An image. Go to that message.',
    );
  });
});
