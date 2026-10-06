import { questionCanvasImageUrl, storageImageKey } from './storage-image-url.util';
import { isStorageImageUrl } from './request-url.util';

describe('storage-image-url.util (issue #365 S2)', () => {
  describe('storageImageKey', () => {
    it('storageImageKey_SignedUrl_DropsQuery', () => {
      expect(storageImageKey('/img/b/questions/q/question.jpg?X-Amz-Signature=a&X-Amz-Date=1')).toBe(
        '/img/b/questions/q/question.jpg'
      );
    });

    it('storageImageKey_TwoSignaturesOfSameObject_ProduceSameKey', () => {
      expect(storageImageKey('/img/b/k.jpg?X-Amz-Signature=one')).toBe(storageImageKey('/img/b/k.jpg?X-Amz-Signature=two'));
    });

    it('storageImageKey_UnsignedUrlOrEmpty_ReturnsInputOrEmpty', () => {
      expect(storageImageKey('/img/b/k.jpg')).toBe('/img/b/k.jpg');
      expect(storageImageKey(null)).toBe('');
      expect(storageImageKey(undefined)).toBe('');
    });
  });

  describe('questionCanvasImageUrl', () => {
    it('questionCanvasImageUrl_V2Present_ReturnsV2Verbatim', () => {
      const v2 = '/img/b/questions/q/question-v2.jpg?X-Amz-Signature=v2';
      expect(questionCanvasImageUrl({ imageUrl: '/img/b/questions/q/question.jpg?X-Amz-Signature=v1', imageUrlV2: v2 })).toBe(v2);
    });

    it('questionCanvasImageUrl_V2NullOrBlank_FallsBackToImageUrl', () => {
      expect(questionCanvasImageUrl({ imageUrl: '/img/b/q.jpg?s=1', imageUrlV2: null })).toBe('/img/b/q.jpg?s=1');
      expect(questionCanvasImageUrl({ imageUrl: '/img/b/q.jpg?s=1', imageUrlV2: '  ' })).toBe('/img/b/q.jpg?s=1');
    });

    it('questionCanvasImageUrl_NothingAvailable_ReturnsNull', () => {
      expect(questionCanvasImageUrl({ imageUrl: '' })).toBeNull();
      expect(questionCanvasImageUrl(null)).toBeNull();
    });
  });

  describe('isStorageImageUrl', () => {
    it('isStorageImageUrl_ImgPrefixedPaths_ReturnsTrue', () => {
      expect(isStorageImageUrl('/img/b/k.jpg?X-Amz-Signature=x')).toBeTrue();
      expect(isStorageImageUrl(`${location.origin}/img/b/k.jpg`)).toBeTrue();
    });

    it('isStorageImageUrl_OtherPaths_ReturnsFalse', () => {
      expect(isStorageImageUrl('/api/exam/img/list')).toBeFalse();
      expect(isStorageImageUrl('/images/logo.png')).toBeFalse();
      expect(isStorageImageUrl('/img')).toBeFalse();
    });
  });
});
