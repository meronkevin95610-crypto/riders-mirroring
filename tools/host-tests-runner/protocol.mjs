// Hand-transpiled from src/shared/protocol.ts — types stripped, structure preserved.
// If the original protocol.ts changes, this file must be re-synced.

export type DeviceType = 'android' | 'ios';

export const PairingPayloadShape = {
  host: 'string',
  port: 'number',
  sessionToken: 'string',
  key: 'string',
  v: 1,
};
