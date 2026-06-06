import type { AttachmentKind, MessageDto, MessageAttachmentDto } from '../types/chat';

export const getAttachmentLabel = (kind?: AttachmentKind | string | null): string => {
  switch (kind) {
    case 'Image':
      return '[Ảnh]';
    case 'Audio':
      return '[Audio]';
    case 'Video':
      return '[Video]';
    default:
      return '[Tệp]';
  }
};

export const buildMessagePreview = (message: Pick<MessageDto, 'content' | 'attachments'>): string => {
  const text = message.content?.trim() ?? '';
  const firstAttachment = message.attachments?.[0];

  if (!firstAttachment) {
    return text;
  }

  const label = getAttachmentLabel(firstAttachment.kind);
  return text ? `${label} ${text}` : label;
};

export const determineMessageType = (
  content: string,
  attachments: MessageAttachmentDto[] | null | undefined
): string => {
  if (content.trim() || !attachments?.length) {
    return 'Text';
  }

  if (attachments.length > 1) {
    return 'File';
  }

  return attachments[0].kind;
};
