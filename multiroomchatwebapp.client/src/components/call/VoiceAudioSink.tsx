import { useEffect, useRef, useState } from 'react';
import { RoomEvent, Track } from 'livekit-client';
import type {
  AudioTrack,
  Participant,
  RemoteParticipant,
  RemoteTrack,
  RemoteTrackPublication,
  TrackPublication,
} from 'livekit-client';
import { useVoiceStore } from '../../store/useVoiceStore';

interface AudioTrackElementProps {
  track: AudioTrack;
  muted?: boolean;
}

const AudioTrackElement = ({ track, muted = false }: AudioTrackElementProps) => {
  const audioRef = useRef<HTMLAudioElement | null>(null);

  useEffect(() => {
    const audioElement = audioRef.current;
    if (!audioElement) return;

    audioElement.muted = muted;
    track.attach(audioElement);

    return () => {
      track.detach(audioElement);
      audioElement.pause();
      audioElement.srcObject = null;
      audioElement.removeAttribute('src');
      audioElement.load();
    };
  }, [track]);

  useEffect(() => {
    const audioElement = audioRef.current;
    if (!audioElement) return;

    audioElement.muted = muted;
  }, [muted]);

  return <audio ref={audioRef} autoPlay muted={muted} />;
};

const getParticipantAudioFocusId = (participant: Participant) =>
  participant.sid || participant.identity || null;

const isPlayableAudioPublication = (
  publication: TrackPublication,
) => {
  if (!publication.audioTrack || publication.isMuted) return false;

  return publication.source === Track.Source.Microphone ||
    publication.source === Track.Source.ScreenShareAudio;
};

const shouldMuteAudioPublication = (
  publication: TrackPublication,
  participant: Participant,
  focusedScreenShareAudioParticipantId: string | null,
) => {
  if (publication.source !== Track.Source.ScreenShareAudio) return false;

  return getParticipantAudioFocusId(participant) !== focusedScreenShareAudioParticipantId;
};

/**
 * Global remote audio sink cho active LiveKit room.
 * Audio khong phu thuoc vao panel dang inline hay mini popup, tranh mat tieng khi UI unmount.
 */
export const VoiceAudioSink = () => {
  const liveKitRoom = useVoiceStore((s) => s.liveKitRoom);
  const isDeafened = useVoiceStore((s) => s.isDeafened);
  const focusedScreenShareAudioParticipantId = useVoiceStore((s) => s.focusedScreenShareAudioParticipantId);
  const [, forceRender] = useState(0);

  useEffect(() => {
    if (!liveKitRoom) return;

    const refresh = () => forceRender((value) => value + 1);
    const handleParticipantChanged = (_participant: RemoteParticipant) => refresh();
    const handleTrackSubscribed = (
      _track: RemoteTrack,
      _publication: RemoteTrackPublication,
      _participant: RemoteParticipant,
    ) => refresh();
    const handleTrackUnsubscribed = (
      _track: RemoteTrack,
      _publication: RemoteTrackPublication,
      _participant: RemoteParticipant,
    ) => refresh();
    const handleTrackPublicationChanged = (
      _publication: TrackPublication,
      _participant: Participant,
    ) => refresh();

    liveKitRoom
      .on(RoomEvent.ParticipantConnected, handleParticipantChanged)
      .on(RoomEvent.ParticipantDisconnected, handleParticipantChanged)
      .on(RoomEvent.TrackSubscribed, handleTrackSubscribed)
      .on(RoomEvent.TrackUnsubscribed, handleTrackUnsubscribed)
      .on(RoomEvent.TrackPublished, handleTrackPublicationChanged)
      .on(RoomEvent.TrackUnpublished, handleTrackPublicationChanged)
      .on(RoomEvent.TrackMuted, handleTrackPublicationChanged)
      .on(RoomEvent.TrackUnmuted, handleTrackPublicationChanged);

    return () => {
      liveKitRoom
        .off(RoomEvent.ParticipantConnected, handleParticipantChanged)
        .off(RoomEvent.ParticipantDisconnected, handleParticipantChanged)
        .off(RoomEvent.TrackSubscribed, handleTrackSubscribed)
        .off(RoomEvent.TrackUnsubscribed, handleTrackUnsubscribed)
        .off(RoomEvent.TrackPublished, handleTrackPublicationChanged)
        .off(RoomEvent.TrackUnpublished, handleTrackPublicationChanged)
        .off(RoomEvent.TrackMuted, handleTrackPublicationChanged)
        .off(RoomEvent.TrackUnmuted, handleTrackPublicationChanged);
    };
  }, [liveKitRoom]);

  if (!liveKitRoom || isDeafened) {
    return null;
  }

  const remoteAudioTracks = Array.from(liveKitRoom.remoteParticipants.values())
    .flatMap((participant) =>
      participant
        .getTrackPublications()
        .filter((publication) =>
          isPlayableAudioPublication(publication)
        )
        .map((publication) => ({
          id: `${participant.sid}-${publication.trackSid}`,
          track: publication.audioTrack as AudioTrack,
          muted: shouldMuteAudioPublication(publication, participant, focusedScreenShareAudioParticipantId),
        })),
    );

  return (
    <>
      {remoteAudioTracks.map(({ id, track, muted }) => (
        <AudioTrackElement key={id} track={track} muted={muted} />
      ))}
    </>
  );
};
