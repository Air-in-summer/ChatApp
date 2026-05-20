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
}

const AudioTrackElement = ({ track }: AudioTrackElementProps) => {
  const audioRef = useRef<HTMLAudioElement | null>(null);

  useEffect(() => {
    const audioElement = audioRef.current;
    if (!audioElement) return;

    track.attach(audioElement);

    return () => {
      track.detach(audioElement);
    };
  }, [track]);

  return <audio ref={audioRef} autoPlay />;
};

/**
 * Global remote audio sink cho active LiveKit room.
 * Audio khong phu thuoc vao panel dang inline hay mini popup, tranh mat tieng khi UI unmount.
 */
export const VoiceAudioSink = () => {
  const liveKitRoom = useVoiceStore((s) => s.liveKitRoom);
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

  if (!liveKitRoom) {
    return null;
  }

  const localParticipant = liveKitRoom.localParticipant;
  const participants = [localParticipant, ...Array.from(liveKitRoom.remoteParticipants.values())];
  const remoteAudioTracks = participants
    .filter((participant) => participant !== localParticipant)
    .flatMap((participant) =>
      participant
        .getTrackPublications()
        .filter((publication) =>
          (publication.source === Track.Source.Microphone || publication.source === Track.Source.ScreenShareAudio) &&
          publication.audioTrack &&
          !publication.isMuted
        )
        .map((publication) => ({
          id: `${participant.sid}-${publication.trackSid}`,
          track: publication.audioTrack as AudioTrack,
        })),
    );

  return (
    <>
      {remoteAudioTracks.map(({ id, track }) => (
        <AudioTrackElement key={id} track={track} />
      ))}
    </>
  );
};
