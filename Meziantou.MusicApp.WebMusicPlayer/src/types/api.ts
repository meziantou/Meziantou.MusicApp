// API Response Types matching the REST API

export interface PlaylistSummary {
  id: string;
  name: string;
  trackCount: number;
  duration: number;
  created: string;
  changed: string;
  sortOrder: number;
}

export interface PlaylistsResponse {
  playlists: PlaylistSummary[];
}

export interface TrackInfo {
  id: string;
  title: string;
  artists: string | null;
  artistId: string | null;
  album: string | null;
  albumId: string | null;
  duration: number;
  track: number | null;
  year: number | null;
  genre: string | null;
  bitRate: number | null;
  size: number;
  contentType: string | null;
  addedDate: string | null;
  replayGainTrackGain: number | null;
  replayGainTrackPeak: number | null;
  replayGainAlbumGain: number | null;
  replayGainAlbumPeak: number | null;
}

export interface PlaylistTracksResponse {
  id: string;
  name: string;
  trackCount: number;
  duration: number;
  created: string;
  changed: string;
  tracks: TrackInfo[];
}

export interface CreatePlaylistRequest {
  name: string;
  comment?: string | null;
  songIds?: string[];
}

export interface UpdatePlaylistRequest {
  name?: string | null;
  comment?: string | null;
  songIds?: string[] | null;
}

export interface AlbumInfo {
  id: string;
  name: string;
  artist: string | null;
  artistId: string | null;
  year: number | null;
  genre: string | null;
  duration: number;
  songCount: number;
  created: string;
}

export interface AlbumsResponse {
  albums: AlbumInfo[];
}

export interface ArtistInfo {
  id: string;
  name: string;
  albumCount: number;
}

export interface ArtistsResponse {
  artists: ArtistInfo[];
}

export interface ScanStatusResponse {
  isScanning: boolean;
  isInitialScanCompleted: boolean;
  scanCount: number;
  lastScanDate: string | null;
}

export interface ErrorResponse {
  error: string;
}
