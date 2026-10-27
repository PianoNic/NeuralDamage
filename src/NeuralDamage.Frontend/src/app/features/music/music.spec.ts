import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import type { SongDto } from '../../core/models';
import { audioSrc, clipOf, formatTime, MusicPlayer } from './music-player';
import { CLIP_SECONDS, defaultStart } from './music-picker';

function song(fullSeconds: number): SongDto {
  return {
    id: 's1',
    deezerId: 1,
    title: 'Space Oddity',
    artist: 'David Bowie',
    durationSeconds: fullSeconds,
    fullSeconds,
    fullPending: false,
  };
}

describe('music', () => {
  it('formats clip times as minutes and seconds', () => {
    expect(formatTime(62)).toBe('1:02');
    expect(formatTime(0)).toBe('0:00');
    expect(formatTime(-3)).toBe('0:00');
  });

  it('starts a picked song a third in, always leaving a whole clip', () => {
    expect(defaultStart(song(318))).toBe(106);
    expect(defaultStart(song(20))).toBe(20 - CLIP_SECONDS);
    expect(defaultStart(song(10))).toBe(0);
  });

  it("plays the API's audio from the API, and Deezer's previews as they are", () => {
    expect(audioSrc('/api/music/songs/s1/full')).toBe(`${environment.apiBaseUrl}/api/music/songs/s1/full`);
    expect(audioSrc('https://cdnt-preview.dzcdn.net/a.mp3')).toBe('https://cdnt-preview.dzcdn.net/a.mp3');
    expect(clipOf('post:1', { songId: 's1', title: 't', artist: 'a', audioUrl: '/x', start: 62, seconds: 15 })).toEqual({
      key: 'post:1',
      url: '/x',
      start: 62,
      seconds: 15,
    });
  });

  it('plays one clip at a time, and toggling the playing one stops it', async () => {
    const played: string[] = [];
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function (this: HTMLMediaElement) {
      played.push(this.src);
      return Promise.resolve();
    });
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => undefined);
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => undefined);
    const player = TestBed.inject(MusicPlayer);

    await player.play({ key: 'post:1', url: '/a', start: 0, seconds: 15 });
    await player.play({ key: 'post:2', url: '/b', start: 0, seconds: 15 });
    expect(player.playing()).toBe('post:2');
    expect(player.isPlaying('post:1')).toBe(false);

    await player.toggle({ key: 'post:2', url: '/b', start: 0, seconds: 15 });
    expect(player.playing()).toBeNull();
    expect(played.length).toBe(2);
  });

  it('marks a clip the browser refused to play, and plays nothing', async () => {
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked', 'NotAllowedError'));
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => undefined);
    const player = TestBed.inject(MusicPlayer);

    await player.play({ key: 'story:1', url: '/a', start: 0, seconds: 30 });

    expect(player.playing()).toBeNull();
    expect(player.failed()).toBe('story:1');
  });
});
