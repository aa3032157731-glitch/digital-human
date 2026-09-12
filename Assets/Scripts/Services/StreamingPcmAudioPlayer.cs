using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(AudioSource))]
public class StreamingPcmAudioPlayer : MonoBehaviour
{
    [Tooltip("AudioSource used for TTS playback. Bind SALSA to this same AudioSource.")]
    public AudioSource audioSource;

    [Tooltip("Dynamic clip length in seconds. The clip loops while new PCM is supplied.")]
    [Range(5, 120)]
    public int clipSeconds = 30;

    [Tooltip("Play after this many seconds of buffered audio to reduce underruns.")]
    [Range(0f, 0.5f)]
    public float startBufferSeconds = 0.12f;

    [Header("Playback Events")]
    public UnityEvent onPlaybackStarted = new UnityEvent();
    public UnityEvent onPlaybackStopped = new UnityEvent();

    public event Action PlaybackStarted;
    public event Action PlaybackStopped;

    private int sampleRate;
    private int channels;
    private int writePosition;
    private int totalWrittenSamples;
    private int totalPlayedSamples;
    private int lastPlaybackPosition;
    private bool streamOpen;
    private bool streamEnded;
    private bool playbackActive;

    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void Update()
    {
        if (audioSource == null || audioSource.clip == null)
        {
            SetPlaybackActive(false);
            return;
        }

        if (!audioSource.isPlaying)
        {
            SetPlaybackActive(false);
            return;
        }

        var position = audioSource.timeSamples;
        var delta = position - lastPlaybackPosition;
        if (delta < 0)
        {
            delta += audioSource.clip.samples;
        }

        totalPlayedSamples += delta;
        lastPlaybackPosition = position;

        if (streamEnded && totalPlayedSamples >= totalWrittenSamples)
        {
            StopPlayback();
        }
    }

    public void BeginStream(int outputSampleRate, int outputChannels = 1)
    {
        sampleRate = Mathf.Max(8000, outputSampleRate);
        channels = Mathf.Max(1, outputChannels);
        ResetState();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        var totalSamples = Mathf.Max(sampleRate * clipSeconds, sampleRate);
        StopPlayback();
        audioSource.loop = true;
        audioSource.clip = AudioClip.Create("Streaming Xunfei TTS", totalSamples, channels, sampleRate, false);
        streamOpen = true;
        streamEnded = false;
    }

    public void AppendPcm16LittleEndian(byte[] pcmBytes)
    {
        if (!streamOpen || pcmBytes == null || pcmBytes.Length < 2)
        {
            return;
        }

        var sampleCount = pcmBytes.Length / 2;
        var floats = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var lo = pcmBytes[i * 2];
            var hi = pcmBytes[i * 2 + 1];
            var value = (short)(lo | (hi << 8));
            floats[i] = value / 32768f;
        }

        WriteToClip(floats);

        TryStartPlayback(force: false);
    }

    public void EndStream()
    {
        streamOpen = false;
        streamEnded = true;

        TryStartPlayback(force: true);

        if (totalWrittenSamples <= 0)
        {
            StopPlayback();
        }
    }

    public void StopAndClear()
    {
        streamOpen = false;

        if (audioSource != null)
        {
            StopPlayback();
            audioSource.clip = null;
        }

        ResetState();
    }

    private void ResetState()
    {
        writePosition = 0;
        totalWrittenSamples = 0;
        totalPlayedSamples = 0;
        lastPlaybackPosition = 0;
        streamEnded = false;
    }

    private void TryStartPlayback(bool force)
    {
        if (audioSource == null || audioSource.clip == null || audioSource.isPlaying)
        {
            return;
        }

        var startSamples = force ? 1 : Mathf.CeilToInt(sampleRate * startBufferSeconds);
        if (totalWrittenSamples < startSamples)
        {
            return;
        }

        totalPlayedSamples = 0;
        lastPlaybackPosition = 0;
        audioSource.timeSamples = 0;
        audioSource.Play();
        SetPlaybackActive(true);
    }

    private void StopPlayback()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        SetPlaybackActive(false);
    }

    private void SetPlaybackActive(bool value)
    {
        if (playbackActive == value)
        {
            return;
        }

        playbackActive = value;

        if (playbackActive)
        {
            PlaybackStarted?.Invoke();
            onPlaybackStarted?.Invoke();
        }
        else
        {
            PlaybackStopped?.Invoke();
            onPlaybackStopped?.Invoke();
        }
    }

    private void WriteToClip(float[] samples)
    {
        if (audioSource == null || audioSource.clip == null || samples == null || samples.Length == 0)
        {
            return;
        }

        var clip = audioSource.clip;
        var offset = 0;

        while (offset < samples.Length)
        {
            var writable = Mathf.Min(samples.Length - offset, clip.samples - writePosition);
            var block = new float[writable];
            System.Array.Copy(samples, offset, block, 0, writable);

            if (!clip.SetData(block, writePosition))
            {
                Debug.LogError("Failed to write streamed PCM data into AudioClip.");
                return;
            }

            writePosition += writable;
            if (writePosition >= clip.samples)
            {
                writePosition = 0;
            }

            offset += writable;
            totalWrittenSamples += writable;
        }
    }
}
