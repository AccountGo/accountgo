# Local OCR models

PaddleOCR models PP-OCRv5_mobile_det and en_PP-OCRv5_mobile_rec come from the
PaddlePaddle project's official PaddleX 3.7.2 model distribution. The conversion
used PaddleOCR 3.7.0 and paddle2onnx 2.1.0.

```sh
paddle2onnx --model_dir <paddlex official_models>/<name> --model_filename inference.json --params_filename inference.pdiparams --save_file <name>.onnx --opset_version 14
```

The recognition dictionary is the model's `PostProcess.character_dict` with 436
entries. CTC output uses class 0 for blank, classes 1–436 for dictionary
entries and class 437 for space. The worker verifies the three pinned
SHA-256 values in [SHA256SUMS](SHA256SUMS) before opening a document.

Copyright (c) 2020 PaddlePaddle Authors. Licensed under the Apache License,
Version 2.0. See the accompanying [license](LICENSE).

Upstream source and model information: [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)
and [PaddleX](https://github.com/PaddlePaddle/PaddleX).
