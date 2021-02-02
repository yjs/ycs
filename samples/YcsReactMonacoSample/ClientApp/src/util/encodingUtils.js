
export class EncodingUtils {
    static stringToByteArray(str) {
        var binary_string = atob(str);
        var len = binary_string.length;
        var bytes = new Uint8Array(len);

        for (var i = 0; i < len; i++) {
            bytes[i] = binary_string.charCodeAt(i);
        }

        return bytes;
    }

    static byteArrayToString(u8) {
        return btoa(String.fromCharCode.apply(null, u8));
    }
}

export default EncodingUtils;
