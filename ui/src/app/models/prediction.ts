export interface Prediction {
    class_id: number;
    x: number;
    y: number;
    width: number;
    height: number;
    subpredictions: Prediction[];
}

export interface PredictionList {
    success: boolean;
    predictions: Prediction[];
}

/**
 * question-detector `GET /labeled-pages`: eğitim veri setine kaydedilmiş sayfa id'leri.
 * Id = sha256(ham görsel baytları).hexdigest()[:16] (`dataset_store.content_id`).
 */
export interface LabeledPagesResponse {
    pageIds: string[];
}
