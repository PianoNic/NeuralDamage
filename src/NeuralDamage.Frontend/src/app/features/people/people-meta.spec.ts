import { canRemoveMember, joinedDate, joinedLabel, roleLabel, visibilityLabel } from './people-meta';

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

  it('gives the full join date and the role', () => {
    expect(joinedDate(new Date(2026, 8, 30, 9).toISOString())).toContain('2026');
    expect(joinedDate('nonsense')).toBe('');
    expect(roleLabel('Owner')).toBe('Owner');
    expect(roleLabel('Member')).toBe('Member');
  });

  it('only offers removal the API allows', () => {
    const owner = { role: 'Owner', userId: 'u1' };
    const alice = { role: 'Member', userId: 'u2' };
    const bot = { role: 'Member', userId: null };
    const members = [owner, alice, bot];
    expect(canRemoveMember(alice, members, 'u1')).toBe(true);
    expect(canRemoveMember(bot, members, 'u1')).toBe(true);
    expect(canRemoveMember(owner, members, 'u1')).toBe(false);
    expect(canRemoveMember(bot, members, 'u2')).toBe(false);
    expect(canRemoveMember(alice, members, 'u2')).toBe(true);
    expect(canRemoveMember(alice, members, null)).toBe(false);
  });
});
