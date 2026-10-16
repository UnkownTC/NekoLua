using NAudio.Vorbis;
using SecretLabNAudio.Core.FileReading;

namespace NekoLua.SecretLabNAudioProxy.Reading;

internal class OggAudioReaderFactory : IAudioReaderFactory
{
    public AudioReaderFactoryResult FromPath(string path)
    {
        return new VorbisWaveReader(path);
    }
    public AudioReaderFactoryResult FromStream(Stream stream, bool closeOnDispose)
    {
        return new VorbisWaveReader(stream, closeOnDispose);
    }
}
