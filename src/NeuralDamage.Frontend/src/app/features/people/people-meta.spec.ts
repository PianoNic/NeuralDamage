import { joinedLabel, visibilityLabel } from './people-meta';

describe('people labels', () => {
  const now = new Date(2026, 8, 30, 12); // a Wednesday

  it('says when someone joined', () => {
    expect(joinedLabel(new Date(2026, 8, 30, 9).toISOString(), now)).toBe('Joined today');
    expect(joinedLabel(new Date(2026, 8, 29, 9).toISOString(), now)).toBe('Joined yesterday');
    const sunday = new Date(2026, 8, 27, 9);
    expect(joinedLabel(sunday.toISOString(), now)).toBe(
      `Joined ${sunday.toLocaleDateString(undefined, { weekday: 'long' })}`,
    );
    expect(joinedLabel('nonsense', now)).toBe('');
  });

  it('describes who made a bot and where it lives', () => {
    const bot = { isPublic: true, createdById: 'u1', createdBy: { displayName: 'alice' } };
    expect(visibilityLabel(bot, 'u2')).toBe('Public, made by alice');
    expect(visibilityLabel(bot, 'u1')).toBe('Public, made by you');
    expect(visibilityLabel({ ...bot, isPublic: false }, 'u1')).toBe('Private to this chat');
  });
});
