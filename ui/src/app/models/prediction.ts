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
