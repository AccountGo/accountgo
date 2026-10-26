/**
 * Native WebAuthn helpers (Zero npm dependencies per locked architecture).
 */

export function isWebAuthnSupported(): boolean {
  return typeof window !== 'undefined' && Boolean(window.PublicKeyCredential);
}

export function bufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

export function base64UrlToBuffer(base64url: string): ArrayBuffer {
  let base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
  while (base64.length % 4 !== 0) {
    base64 += '=';
  }
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

export async function createPasskeyCredential(options: any): Promise<any> {
  if (!isWebAuthnSupported()) {
    throw new Error('WebAuthn is not supported on this browser or device.');
  }

  const publicKey: CredentialCreationOptions['publicKey'] = {
    ...options,
    challenge: base64UrlToBuffer(options.challenge),
    user: {
      ...options.user,
      id: base64UrlToBuffer(options.user.id)
    },
    excludeCredentials: options.excludeCredentials?.map((cred: any) => ({
      ...cred,
      id: base64UrlToBuffer(cred.id)
    }))
  };

  const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential;
  if (!credential) {
    throw new Error('Credential creation returned null.');
  }

  const response = credential.response as AuthenticatorAttestationResponse;
  const transports = typeof (response as any).getTransports === 'function' ? (response as any).getTransports() : [];

  return {
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      attestationObject: bufferToBase64Url(response.attestationObject),
      clientDataJson: bufferToBase64Url(response.clientDataJSON),
      transports
    }
  };
}

export async function getPasskeyAssertion(options: any): Promise<any> {
  if (!isWebAuthnSupported()) {
    throw new Error('WebAuthn is not supported on this browser or device.');
  }

  const publicKey: CredentialRequestOptions['publicKey'] = {
    ...options,
    challenge: base64UrlToBuffer(options.challenge),
    allowCredentials: options.allowCredentials?.map((cred: any) => ({
      ...cred,
      id: base64UrlToBuffer(cred.id)
    }))
  };

  const assertion = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential;
  if (!assertion) {
    throw new Error('Assertion returned null.');
  }

  const response = assertion.response as AuthenticatorAssertionResponse;

  return {
    id: assertion.id,
    rawId: bufferToBase64Url(assertion.rawId),
    type: assertion.type,
    response: {
      authenticatorData: bufferToBase64Url(response.authenticatorData),
      clientDataJson: bufferToBase64Url(response.clientDataJSON),
      signature: bufferToBase64Url(response.signature),
      userHandle: response.userHandle ? bufferToBase64Url(response.userHandle) : null
    }
  };
}
