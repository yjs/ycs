export function stringToByteArray(str: string): Uint8Array {
  const binaryStr = atob(str);
  const len = binaryStr.length;
  const bytes = new Uint8Array(len);

  for (let i = 0; i < len; i++) {
    bytes[i] = binaryStr.charCodeAt(i);
  }

  return bytes;
}

export function byteArrayToString(u8: Uint8Array): string {
  let str: string = '';

  u8.forEach(byte => {
    str += String.fromCharCode(byte);
  });

  return btoa(str);
}
