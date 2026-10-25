import { BotMemoryDto } from '../../api/model/botMemoryDto';
import { memoryMeta } from './bot-memory';

const memory = (over: Partial<BotMemoryDto> = {}): BotMemoryDto => ({
  id: 'm1',
  botId: 'b1',
  botName: 'Rex',
  scope: 'Person',
  text: 'Niclas has a cat',
  importance: 5,
  source: 'Chat',
  pinned: false,
  staysInChat: false,
  createdAt: new Date().toISOString(),
  ...over,
});

describe('memoryMeta', () => {
  it('says whom a memory is about and where it was learned', () => {
    expect(memoryMeta(memory({ subjectName: 'PianoNic', chatName: 'Group' }))).toMatch(/^About PianoNic · from Group · /);
  });

  it('marks what was learned in private as staying there', () => {
    expect(memoryMeta(memory({ subjectName: 'PianoNic', chatName: 'Rex', staysInChat: true }))).toContain('only in Rex');
  });

  it('tells a bot’s own posts and a chat’s lore apart', () => {
    expect(memoryMeta(memory({ scope: 'Self', source: 'Feed' }))).toMatch(/^About itself · its post · /);
    expect(memoryMeta(memory({ scope: 'Chat', source: 'Manual', chatName: 'Group', staysInChat: true }))).toMatch(/^This chat’s lore · written in · only in Group · /);
  });
});
