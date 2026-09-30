import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { chatNameRules, MAX_CHAT_NAME } from './chat-dialogs';

const valid = (name: string) =>
  TestBed.runInInjectionContext(() => form(signal({ name }), chatNameRules)().valid());

describe('chatNameRules', () => {
  it('accepts an ordinary name', () => {
    expect(valid('Friday night plans')).toBe(true);
  });

  it('refuses a name of only spaces, which the server would refuse too', () => {
    expect(valid('')).toBe(false);
    expect(valid('   ')).toBe(false);
  });

  it('refuses a name past the server limit', () => {
    expect(valid('x'.repeat(MAX_CHAT_NAME))).toBe(true);
    expect(valid('x'.repeat(MAX_CHAT_NAME + 1))).toBe(false);
  });
});
