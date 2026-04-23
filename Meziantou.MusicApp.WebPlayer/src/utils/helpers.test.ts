import { describe, it, expect } from 'vitest';
import type { TrackInfo } from '../types';
import { matchesSearch, normalizeSearch, sortTracks } from './helpers';

describe('Search Track Feature', () => {
  describe('normalizeSearch', () => {
    it('should convert text to lowercase', () => {
      expect(normalizeSearch('HELLO')).toBe('hello');
      expect(normalizeSearch('Hello World')).toBe('hello world');
    });

    it('should remove diacritics', () => {
      expect(normalizeSearch('café')).toBe('cafe');
      expect(normalizeSearch('naïve')).toBe('naive');
      expect(normalizeSearch('São Paulo')).toBe('sao paulo');
      expect(normalizeSearch('Zürich')).toBe('zurich');
    });

    it('should handle accented characters', () => {
      expect(normalizeSearch('résumé')).toBe('resume');
      expect(normalizeSearch('crème brûlée')).toBe('creme brulee');
      expect(normalizeSearch('Beyoncé')).toBe('beyonce');
    });

    it('should handle multiple accents on same character', () => {
      expect(normalizeSearch('ệ')).toBe('e');
      expect(normalizeSearch('ễ')).toBe('e');
    });

    it('should handle empty strings', () => {
      expect(normalizeSearch('')).toBe('');
    });

    it('should handle special characters', () => {
      expect(normalizeSearch('hello-world')).toBe('hello-world');
      expect(normalizeSearch('test@123')).toBe('test@123');
    });

    it('should normalize whitespace', () => {
      expect(normalizeSearch('  Hello   World  ')).toBe('hello world');
      expect(normalizeSearch('Track\t\tName')).toBe('track name');
    });
  });

  describe('matchesSearch', () => {
    describe('basic matching', () => {
      it('should match exact text', () => {
        expect(matchesSearch('Hello World', 'Hello World')).toBe(true);
      });

      it('should match case-insensitively', () => {
        expect(matchesSearch('Hello World', 'hello world')).toBe(true);
        expect(matchesSearch('hello world', 'HELLO WORLD')).toBe(true);
        expect(matchesSearch('HeLLo WoRLd', 'hElLo wOrLd')).toBe(true);
      });

      it('should match partial text', () => {
        expect(matchesSearch('Hello World', 'Hello')).toBe(true);
        expect(matchesSearch('Hello World', 'World')).toBe(true);
        expect(matchesSearch('Hello World', 'lo Wo')).toBe(true);
      });

      it('should match all query fragments regardless of order', () => {
        expect(matchesSearch('Hello World', 'hello world')).toBe(true);
        expect(matchesSearch('Hello World', 'world hello')).toBe(true);
        expect(matchesSearch('abc xyz def', 'abc def')).toBe(true);
      });

      it('should not match when one fragment is missing', () => {
        expect(matchesSearch('Hello World', 'hello moon')).toBe(false);
        expect(matchesSearch('abc xyz', 'abc def')).toBe(false);
      });

      it('should not match when text does not contain query', () => {
        expect(matchesSearch('Hello World', 'Goodbye')).toBe(false);
        expect(matchesSearch('Hello World', 'xyz')).toBe(false);
      });
    });

    describe('accent-insensitive matching', () => {
      it('should match text with accents against query without accents', () => {
        expect(matchesSearch('café', 'cafe')).toBe(true);
        expect(matchesSearch('résumé', 'resume')).toBe(true);
        expect(matchesSearch('naïve', 'naive')).toBe(true);
      });

      it('should match text without accents against query with accents', () => {
        expect(matchesSearch('cafe', 'café')).toBe(true);
        expect(matchesSearch('resume', 'résumé')).toBe(true);
        expect(matchesSearch('naive', 'naïve')).toBe(true);
      });

      it('should match when both have different accents', () => {
        expect(matchesSearch('café', 'cafè')).toBe(true);
        expect(matchesSearch('naïve', 'naive')).toBe(true);
      });
    });

    describe('edge cases', () => {
      it('should return true for empty query with non-empty text', () => {
        expect(matchesSearch('Hello World', '')).toBe(true);
      });

      it('should return false for empty text with empty query', () => {
        expect(matchesSearch('', '')).toBe(false);
      });

      it('should return false for null or undefined text', () => {
        expect(matchesSearch(null, 'query')).toBe(false);
        expect(matchesSearch(undefined, 'query')).toBe(false);
      });

      it('should return false for null or undefined text with empty query', () => {
        expect(matchesSearch(null, '')).toBe(false);
        expect(matchesSearch(undefined, '')).toBe(false);
      });

      it('should handle whitespace in text', () => {
        expect(matchesSearch('  Hello World  ', 'Hello')).toBe(true);
        expect(matchesSearch('  Hello World  ', 'hello')).toBe(true);
      });

      it('should normalize whitespace in query', () => {
        expect(matchesSearch('Hello World', '  Hello  ')).toBe(true);
        expect(matchesSearch('Hello World', 'Hello   World')).toBe(true);
        expect(matchesSearch('  Hello  ', 'Hello')).toBe(true);
      });

      it('should ignore trailing spaces in query', () => {
        expect(matchesSearch('abc', 'abc')).toBe(true);
        expect(matchesSearch('abc', 'abc ')).toBe(true);
      });
    });

    describe('real-world music search scenarios', () => {
      it('should match song titles', () => {
        expect(matchesSearch('Bohemian Rhapsody', 'bohemian')).toBe(true);
        expect(matchesSearch('Bohemian Rhapsody', 'rhapsody')).toBe(true);
        expect(matchesSearch('Bohemian Rhapsody', 'rhap')).toBe(true);
      });

      it('should match artist names with accents', () => {
        expect(matchesSearch('Beyoncé', 'beyonce')).toBe(true);
        expect(matchesSearch('Björk', 'bjork')).toBe(true);
        expect(matchesSearch('Sigur Rós', 'sigur ros')).toBe(true);
      });

      it('should match album names', () => {
        expect(matchesSearch('The Dark Side of the Moon', 'dark side')).toBe(true);
        expect(matchesSearch('The Dark Side of the Moon', 'moon')).toBe(true);
        expect(matchesSearch('Crème de la Crème', 'creme de la creme')).toBe(true);
      });

      it('should handle special characters in music titles', () => {
        expect(matchesSearch('Don\'t Stop Believin\'', 'stop')).toBe(true);
        expect(matchesSearch('P.Y.T. (Pretty Young Thing)', 'pretty')).toBe(true);
        expect(matchesSearch('Rock & Roll', 'rock')).toBe(true);
        expect(matchesSearch('Rock & Roll', 'roll')).toBe(true);
      });

      it('should match partial artist or band names', () => {
        expect(matchesSearch('Led Zeppelin', 'zep')).toBe(true);
        expect(matchesSearch('Pink Floyd', 'pink')).toBe(true);
        expect(matchesSearch('The Beatles', 'beatles')).toBe(true);
      });

      it('should handle multi-language titles', () => {
        expect(matchesSearch('La Vie en Rose', 'vie')).toBe(true);
        expect(matchesSearch('Así fue', 'asi fue')).toBe(true);
        expect(matchesSearch('São Paulo', 'sao')).toBe(true);
      });

      it('should not match completely different text', () => {
        expect(matchesSearch('Bohemian Rhapsody', 'hotel california')).toBe(false);
        expect(matchesSearch('The Beatles', 'rolling stones')).toBe(false);
      });
    });

    describe('performance and edge cases', () => {
      it('should handle very long strings', () => {
        const longText = 'a'.repeat(10000);
        expect(matchesSearch(longText, 'a')).toBe(true);
        expect(matchesSearch(longText, 'b')).toBe(false);
      });

      it('should handle unicode characters', () => {
        expect(matchesSearch('🎵 Music', 'music')).toBe(true);
        expect(matchesSearch('Song 🎸', 'song')).toBe(true);
      });

      it('should handle numbers', () => {
        expect(matchesSearch('Track 1', '1')).toBe(true);
        expect(matchesSearch('2023 Album', '2023')).toBe(true);
      });
    });
  });

  describe('sortTracks', () => {
    const createTrack = (id: string, title: string, addedDate: string | null): TrackInfo => ({
      id,
      title,
      path: `/music/${id}.mp3`,
      artists: null,
      album: null,
      duration: 60,
      artistId: null,
      albumId: null,
      track: null,
      year: null,
      genre: null,
      bitRate: null,
      size: 1000,
      contentType: 'audio/mpeg',
      addedDate,
      isrc: null,
      replayGainTrackGain: null,
      replayGainTrackPeak: null,
      replayGainAlbumGain: null,
      replayGainAlbumPeak: null,
    });

    it('should sort by added date descending', () => {
      const tracks = [
        createTrack('old', 'Old Track', '2024-01-01T00:00:00Z'),
        createTrack('new', 'New Track', '2024-03-01T00:00:00Z'),
        createTrack('mid', 'Mid Track', '2024-02-01T00:00:00Z'),
      ];

      const sorted = sortTracks(tracks, 'added', 'desc');

      expect(sorted.map(track => track.id)).toEqual(['new', 'mid', 'old']);
    });

    it('should preserve original order when added dates are equal', () => {
      const tracks = [
        createTrack('a', 'Track A', '2024-01-01T00:00:00Z'),
        createTrack('b', 'Track B', '2024-01-01T00:00:00Z'),
        createTrack('c', 'Track C', '2024-01-01T00:00:00Z'),
      ];

      const sorted = sortTracks(tracks, 'added', 'desc');

      expect(sorted.map(track => track.id)).toEqual(['a', 'b', 'c']);
    });
  });
});
