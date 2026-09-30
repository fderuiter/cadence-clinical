import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import Hls from "hls.js";
import MediaPreviewModal from "../src/components/fileshare/MediaPreviewModal.vue";
import { fileshareService } from "../src/api/fileshare";
import { useAuthStore } from "../src/stores/auth";

vi.mock("../src/api/fileshare", () => ({
  fileshareService: {
    getStreamUrl: vi.fn(),
    recordPlaybackAudit: vi.fn(),
  },
}));

describe("feat(fileshare): 09 — HLS Adaptive Streaming in Media Player (#4316)", () => {
  const mockVideoFile = {
    id: "file-video-001",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Investigator_Training_Module_1.mp4",
    mime_type: "video/mp4",
    size_bytes: 104857600,
    download_url: "https://example.com/raw/video.mp4",
    transcode_status: "READY",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    setActivePinia(createPinia());
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.user = { username: "lead_cra_01" };
    authStore.rawRoles = ["CRA"];

    // Default mock response for stream-url (Ready)
    fileshareService.getStreamUrl.mockResolvedValue({
      file_id: "file-video-001",
      stream_url: "https://example.com/hls/master.m3u8",
      expires_in: 300,
      transcode_status: "READY",
      message: "Video stream is ready",
    });

    fileshareService.recordPlaybackAudit.mockResolvedValue({
      id: "audit-001",
      file_record_id: "file-video-001",
      event: "PLAY_START",
      session_id: "test-session",
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("calls getStreamUrl and initializes HLS.js when video is READY", async () => {
    const originalIsSupported = Hls.isSupported;
    Hls.isSupported = vi.fn(() => true);

    const loadSourceSpy = vi.fn();
    const attachMediaSpy = vi.fn();
    const onSpy = vi.fn();
    const destroySpy = vi.fn();

    vi.spyOn(Hls.prototype, "loadSource").mockImplementation(loadSourceSpy);
    vi.spyOn(Hls.prototype, "attachMedia").mockImplementation(attachMediaSpy);
    vi.spyOn(Hls.prototype, "on").mockImplementation(onSpy);
    vi.spyOn(Hls.prototype, "destroy").mockImplementation(destroySpy);

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
        permissions: { canDownload: true },
      },
    });

    await flushPromises();

    expect(fileshareService.getStreamUrl).toHaveBeenCalledWith(
      "file-video-001"
    );
    expect(wrapper.find(".video-player-wrapper").exists()).toBe(true);
    expect(wrapper.find("video.video-element").exists()).toBe(true);

    Hls.isSupported = originalIsSupported;
    wrapper.unmount();
  });

  it("falls back to native HLS on Safari when Hls.isSupported() is false", async () => {
    const originalIsSupported = Hls.isSupported;
    Hls.isSupported = vi.fn(() => false);

    const originalCanPlayType = HTMLMediaElement.prototype.canPlayType;
    HTMLMediaElement.prototype.canPlayType = vi.fn((type) => {
      if (type === "application/vnd.apple.mpegurl") return "probably";
      return "";
    });

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
      },
    });

    await flushPromises();

    expect(fileshareService.getStreamUrl).toHaveBeenCalledWith(
      "file-video-001"
    );
    const video = wrapper.find("video.video-element");
    expect(video.exists()).toBe(true);
    expect(video.attributes("src") || video.element.src).toContain(
      "master.m3u8"
    );

    Hls.isSupported = originalIsSupported;
    HTMLMediaElement.prototype.canPlayType = originalCanPlayType;
    wrapper.unmount();
  });

  it("renders 'Processing video' placeholder and polls when status is PENDING (HTTP 202)", async () => {
    fileshareService.getStreamUrl.mockResolvedValueOnce({
      file_id: "file-video-001",
      stream_url: null,
      expires_in: 300,
      transcode_status: "PENDING",
      message: "Video transcoding is pending",
    });

    const pendingVideoFile = {
      ...mockVideoFile,
      transcode_status: "PENDING",
    };

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: pendingVideoFile,
      },
    });

    await flushPromises();

    expect(wrapper.find(".video-processing-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("Processing video — check back soon");
    expect(wrapper.find(".spinner-circle").exists()).toBe(true);

    fileshareService.getStreamUrl.mockResolvedValueOnce({
      file_id: "file-video-001",
      stream_url: "https://example.com/hls/master.m3u8",
      expires_in: 300,
      transcode_status: "READY",
      message: "Video stream is ready",
    });

    vi.advanceTimersByTime(10000);
    await flushPromises();

    expect(fileshareService.getStreamUrl).toHaveBeenCalledTimes(2);
    expect(wrapper.find(".video-player-wrapper").exists()).toBe(true);

    wrapper.unmount();
  });

  it("displays timeout warning after 10 failed polling attempts", async () => {
    fileshareService.getStreamUrl.mockResolvedValue({
      file_id: "file-video-001",
      stream_url: null,
      expires_in: 300,
      transcode_status: "IN_PROGRESS",
      message: "Video transcoding is in progress",
    });

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: { ...mockVideoFile, transcode_status: "IN_PROGRESS" },
      },
    });

    await flushPromises();

    for (let i = 0; i < 10; i++) {
      vi.advanceTimersByTime(10000);
      await flushPromises();
    }

    const timeoutCard = wrapper.find(".timeout-card");
    expect(timeoutCard.exists()).toBe(true);
    expect(timeoutCard.text()).toContain(
      "Video processing is taking longer than expected — try again later"
    );
    expect(timeoutCard.find("button").text()).toContain("Check Again");

    wrapper.unmount();
  });

  it("displays error card when transcoding fails", async () => {
    fileshareService.getStreamUrl.mockRejectedValueOnce({
      status: 409,
      data: {
        detail: "Video transcoding failed: Corrupt MOOV atom in input stream.",
      },
    });

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
      },
    });

    await flushPromises();

    expect(wrapper.find(".error-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("Video Transcoding Failed");
    expect(wrapper.text()).toContain("Corrupt MOOV atom");

    wrapper.unmount();
  });

  it("allows changing quality levels via quality selector", async () => {
    const originalIsSupported = Hls.isSupported;
    Hls.isSupported = vi.fn(() => true);

    let manifestCallback = null;
    vi.spyOn(Hls.prototype, "loadSource").mockImplementation(vi.fn());
    vi.spyOn(Hls.prototype, "attachMedia").mockImplementation(vi.fn());
    vi.spyOn(Hls.prototype, "on").mockImplementation((event, cb) => {
      if (event === Hls.Events.MANIFEST_PARSED) {
        manifestCallback = cb;
      }
    });
    vi.spyOn(Hls.prototype, "destroy").mockImplementation(vi.fn());

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
      },
    });

    await flushPromises();

    if (manifestCallback) {
      manifestCallback(Hls.Events.MANIFEST_PARSED, {
        levels: [
          { height: 360, bitrate: 800000 },
          { height: 720, bitrate: 2500000 },
          { height: 1080, bitrate: 5000000 },
        ],
      });
    }

    await wrapper.vm.$nextTick();

    const qualitySelect = wrapper.find("select.quality-select");
    expect(qualitySelect.exists()).toBe(true);
    const options = qualitySelect.findAll("option");
    expect(options.length).toBe(4);
    expect(options[1].text()).toBe("360p");
    expect(options[2].text()).toBe("720p");
    expect(options[3].text()).toBe("1080p");

    await qualitySelect.setValue(2);
    expect(wrapper.text()).toContain("[1080p]");

    Hls.isSupported = originalIsSupported;
    wrapper.unmount();
  });

  it("fires Part 11 playback checkpoint audit events on play, 25%, 50%, 75%, and end", async () => {
    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
      },
    });

    await flushPromises();

    const video = wrapper.find("video.video-element");
    expect(video.exists()).toBe(true);

    // 1. Play event -> PLAY_START
    await video.trigger("playing");
    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        file_record_id: "file-video-001",
        event: "PLAY_START",
        source: "internal",
        session_id: expect.any(String),
      })
    );

    // 2. Time update to 30% -> PLAY_25
    Object.defineProperty(video.element, "duration", {
      value: 100,
      configurable: true,
    });
    Object.defineProperty(video.element, "currentTime", {
      value: 30,
      configurable: true,
      writable: true,
    });
    await video.trigger("timeupdate");

    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        event: "PLAY_25",
      })
    );

    // 3. Time update to 60% -> PLAY_50
    video.element.currentTime = 60;
    await video.trigger("timeupdate");
    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        event: "PLAY_50",
      })
    );

    // 4. Time update to 85% -> PLAY_75
    video.element.currentTime = 85;
    await video.trigger("timeupdate");
    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        event: "PLAY_75",
      })
    );

    // 5. Ended event -> PLAY_END
    await video.trigger("ended");
    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        event: "PLAY_END",
      })
    );

    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledTimes(5);
    await video.trigger("playing");
    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledTimes(5);

    wrapper.unmount();
  });

  it("passes guest_link source and token for external guest reviewer mode", async () => {
    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: mockVideoFile,
        isGuestView: true,
        guestToken: "gxt_test_token_123",
      },
    });

    await flushPromises();

    const video = wrapper.find("video.video-element");
    await video.trigger("playing");

    expect(fileshareService.recordPlaybackAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        file_record_id: "file-video-001",
        event: "PLAY_START",
        source: "guest_link",
        guest_token: "gxt_test_token_123",
      })
    );

    wrapper.unmount();
  });

  it("leaves non-video files (PDF, image, audio) unaffected without HLS or polling", async () => {
    const pdfFile = {
      id: "file-pdf-001",
      filename: "Protocol_v1.pdf",
      mime_type: "application/pdf",
      study_id: "STUDY-USDM-001",
    };

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord: pdfFile,
      },
    });

    await flushPromises();

    expect(fileshareService.getStreamUrl).not.toHaveBeenCalled();
    expect(wrapper.find(".pdf-document-sheet").exists()).toBe(true);
    expect(wrapper.find("video").exists()).toBe(false);

    wrapper.unmount();
  });
});
