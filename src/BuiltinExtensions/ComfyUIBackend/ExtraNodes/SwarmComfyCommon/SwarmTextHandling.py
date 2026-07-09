import itertools
import numpy
import torch, comfy
from comfy import model_management
from comfy.sdxl_clip import SDXLClipModel, SDXLRefinerClipModel, SDXLClipG
from nodes import MAX_RESOLUTION

try:
    from comfy.text_encoders.sd3_clip import SD3ClipModel
except ImportError:
    try:
        from comfy.sd3_clip import SD3ClipModel
    except ImportError:
        SD3ClipModel = None


# LLaMA template for Hunyuan Image2Video.
# This is actually a single-line monstrosity due to the way it's formatted.
# This is probably an accident from the python devs misunderstanding how string lines work,
# but, well, we're just matching what they did and that's what they did.
PROMPT_TEMPLATE_ENCODE_VIDEO_I2V = (
    "<|start_header_id|>system<|end_header_id|>\n\n<image>\nDescribe the video by detailing the following aspects according to the reference image: "
    "1. The main content and theme of the video."
    "2. The color, shape, size, texture, quantity, text, and spatial relationships of the objects."
    "3. Actions, events, behaviors temporal relationships, physical movement changes of the objects."
    "4. background environment, light, style and atmosphere."
    "5. camera angles, movements, and transitions used in the video:<|eot_id|>\n\n"
    "<|start_header_id|>user<|end_header_id|>\n\n{}<|eot_id|>"
    "<|start_header_id|>assistant<|end_header_id|>\n\n"
)
# LLaMA template for Qwen Image Edit Plus.
PROMPT_TEMPLATE_QWEN_IMAGE_EDIT_PLUS = "<|im_start|>system\nDescribe the key features of the input image (color, shape, size, texture, objects, background), then explain how the user's text instruction should alter or modify the image. Generate a new image that meets the user's requirements while maintaining consistency with the original input where appropriate.<|im_end|>\n<|im_start|>user\n{}<|im_end|>\n<|im_start|>assistant\n"

def grouped(iterable, count):
    iterator = iter(iterable)
    while True:
        chunk = list(itertools.islice(iterator, count))
        if not chunk:
            return
        yield chunk


def normalize_weight_magnitude(weight, token_count):
    delta = weight - 1
    return 1 + numpy.sign(delta) * numpy.sqrt(numpy.abs(delta) ** 2 / token_count)


def divide_length(word_ids, weights):
    counts = dict(zip(*numpy.unique(word_ids, return_counts=True)))
    counts[0] = 1
    return [[normalize_weight_magnitude(weight, counts[word_id]) if word_id != 0 else 1.0 for weight, word_id in zip(weight_row, word_row)] for weight_row, word_row in zip(weights, word_ids)]


def shift_mean_weight(word_ids, weights):
    word_weights = [weight for weight_row, word_row in zip(weights, word_ids) for weight, word_id in zip(weight_row, word_row) if word_id != 0]
    if len(word_weights) == 0:
        return weights
    delta = 1 - numpy.mean(word_weights)
    return [[weight if word_id == 0 else weight + delta for weight, word_id in zip(weight_row, word_row)] for weight_row, word_row in zip(weights, word_ids)]


def scale_to_norm(weights, word_ids, weight_max):
    top = numpy.max(weights)
    if top == 0:
        return weights
    weight_max = min(top, weight_max)
    return [[weight_max if word_id == 0 else (weight / top) * weight_max for weight, word_id in zip(weight_row, word_row)] for weight_row, word_row in zip(weights, word_ids)]


def from_zero(weights, base_emb):
    weight_tensor = torch.tensor(weights, dtype=base_emb.dtype, device=base_emb.device)
    weight_tensor = weight_tensor.reshape(1, -1, 1).expand(base_emb.shape)
    return base_emb * weight_tensor


def mask_word_id(tokens, word_ids, target_id, mask_token):
    new_tokens = [[mask_token if word_id == target_id else token for token, word_id in zip(token_row, word_row)] for token_row, word_row in zip(tokens, word_ids)]
    mask = numpy.array(word_ids) == target_id
    return new_tokens, mask


def batched_clip_encode(tokens, length, encode_func, num_chunks):
    embeddings = []
    for token_group in grouped(tokens, 32):
        encoded, pooled = encode_func(token_group)
        encoded = encoded.reshape((len(token_group), length, -1))
        embeddings.append(encoded)
    embeddings = torch.cat(embeddings)
    return embeddings.reshape((len(tokens) // num_chunks, length * num_chunks, -1))


def from_masked(tokens, weights, word_ids, base_emb, length, encode_func, mask_token=266):
    pooled_base = base_emb[0, length - 1:length, :]
    unique_word_ids, indices = numpy.unique(numpy.array(word_ids).reshape(-1), return_index=True)
    weight_dict = dict((word_id, weight) for word_id, weight in zip(unique_word_ids, numpy.array(weights).reshape(-1)[indices]) if weight != 1.0)
    if len(weight_dict) == 0:
        return torch.zeros_like(base_emb), pooled_base
    weight_tensor = torch.tensor(weights, dtype=base_emb.dtype, device=base_emb.device)
    weight_tensor = weight_tensor.reshape(1, -1, 1).expand(base_emb.shape)
    mask_token = (mask_token, 1.0)
    weights_to_apply = []
    masked_tokens = []
    masks = []
    for word_id, weight in weight_dict.items():
        masked, mask = mask_word_id(tokens, word_ids, word_id, mask_token)
        masked_tokens.extend(masked)
        mask = torch.tensor(mask, dtype=base_emb.dtype, device=base_emb.device)
        mask = mask.reshape(1, -1, 1).expand(base_emb.shape)
        masks.append(mask)
        weights_to_apply.append(weight)
    embeddings = batched_clip_encode(masked_tokens, length, encode_func, len(tokens))
    masks = torch.cat(masks)
    embeddings = base_emb.expand(embeddings.shape) - embeddings
    pooled = embeddings[0, length - 1:length, :]
    embeddings *= masks
    embeddings = embeddings.sum(dim=0, keepdim=True)
    pooled_start = pooled_base.expand(len(weights_to_apply), -1)
    weights_to_apply = torch.tensor(weights_to_apply, dtype=base_emb.dtype, device=base_emb.device).reshape(-1, 1).expand(pooled_start.shape)
    pooled = (pooled - pooled_start) * (weights_to_apply - 1)
    pooled = pooled.mean(dim=0, keepdim=True)
    return (weight_tensor - 1) * embeddings, pooled_base + pooled


def mask_indices(tokens, indices, mask_token):
    clip_len = len(tokens[0])
    indices_set = set(indices)
    return [[mask_token if row_index * clip_len + token_index in indices_set else token for token_index, token in enumerate(token_row)] for row_index, token_row in enumerate(tokens)]


def down_weight(tokens, weights, word_ids, base_emb, length, encode_func, mask_token=266):
    unique_weights, inverse = numpy.unique(weights, return_inverse=True)
    if numpy.sum(unique_weights < 1) == 0:
        return base_emb, tokens, base_emb[0, length - 1:length, :]
    mask_token = (mask_token, 1.0)
    masked_tokens = []
    masked_current = tokens
    for index in range(len(unique_weights)):
        if unique_weights[index] >= 1:
            continue
        masked_current = mask_indices(masked_current, numpy.where(inverse == index)[0], mask_token)
        masked_tokens.extend(masked_current)
    embeddings = batched_clip_encode(masked_tokens, length, encode_func, len(tokens))
    embeddings = torch.cat([base_emb, embeddings])
    unique_weights = unique_weights[unique_weights <= 1.0]
    weight_mix = numpy.diff([0] + unique_weights.tolist())
    weight_mix = torch.tensor(weight_mix, dtype=embeddings.dtype, device=embeddings.device).reshape((-1, 1, 1))
    weighted_emb = (weight_mix * embeddings).sum(dim=0, keepdim=True)
    return weighted_emb, masked_current, weighted_emb[0, length - 1:length, :]


def a1111_renorm(base_emb, weighted_emb):
    return (base_emb.mean() / weighted_emb.mean()) * weighted_emb


def advanced_encode_from_tokens(tokenized, token_normalization, weight_interpretation, encode_func, weight_max=1.0, return_pooled=False, apply_to_pooled=False):
    tokens = [[token for token, _, _ in token_row] for token_row in tokenized]
    weights = [[weight for _, weight, _ in token_row] for token_row in tokenized]
    word_ids = [[word_id for _, _, word_id in token_row] for token_row in tokenized]
    length = len(tokens[0])
    if token_normalization.startswith("length"):
        weights = divide_length(word_ids, weights)
    if token_normalization.endswith("mean"):
        weights = shift_mean_weight(word_ids, weights)
    pooled = None
    pooled_base = None
    if weight_interpretation == "comfy":
        weighted_tokens = [[(token, weight) for token, weight in zip(token_row, weight_row)] for token_row, weight_row in zip(tokens, weights)]
        weighted_emb, pooled_base = encode_func(weighted_tokens)
        pooled = pooled_base
    else:
        unweighted_tokens = [[(token, 1.0) for token, _, _ in token_row] for token_row in tokenized]
        base_emb, pooled_base = encode_func(unweighted_tokens)
    if weight_interpretation == "A1111":
        weighted_emb = from_zero(weights, base_emb)
        weighted_emb = a1111_renorm(base_emb, weighted_emb)
        pooled = pooled_base
    if weight_interpretation == "compel":
        pos_tokens = [[(token, weight) if weight >= 1.0 else (token, 1.0) for token, weight in zip(token_row, weight_row)] for token_row, weight_row in zip(tokens, weights)]
        weighted_emb, _ = encode_func(pos_tokens)
        weighted_emb, _, pooled = down_weight(pos_tokens, weights, word_ids, weighted_emb, length, encode_func)
    if weight_interpretation == "comfy++":
        weighted_emb, tokens_down, _ = down_weight(unweighted_tokens, weights, word_ids, base_emb, length, encode_func)
        weights = [[weight if weight > 1.0 else 1.0 for weight in weight_row] for weight_row in weights]
        embeddings, pooled = from_masked(unweighted_tokens, weights, word_ids, base_emb, length, encode_func)
        weighted_emb += embeddings
    if weight_interpretation == "down_weight":
        weights = scale_to_norm(weights, word_ids, weight_max)
        weighted_emb, _, pooled = down_weight(unweighted_tokens, weights, word_ids, base_emb, length, encode_func)
    if return_pooled:
        if apply_to_pooled:
            return weighted_emb, pooled
        return weighted_emb, pooled_base
    return weighted_emb, None


def encode_token_weights_for_model(model, token_weight_pairs, encode_func):
    model.cond_stage_model.reset_clip_options()
    if model.layer_idx is not None:
        model.cond_stage_model.set_clip_options({"layer": model.layer_idx})
    model_management.load_model_gpu(model.patcher)
    model.cond_stage_model.set_clip_options({"execution_device": model.patcher.load_device})
    return encode_func(model.cond_stage_model, token_weight_pairs)


def encode_token_weights_l(model, token_weight_pairs):
    output = model.clip_l.encode_token_weights(token_weight_pairs)
    return output[0], output[1] if len(output) > 1 else None


def encode_token_weights_g(model, token_weight_pairs):
    output = model.clip_g.encode_token_weights(token_weight_pairs)
    return output[0], output[1] if len(output) > 1 else None


def encode_token_weights_t5(model, token_weight_pairs):
    output = model.t5xxl.encode_token_weights(token_weight_pairs)
    return output[0], output[1] if len(output) > 1 else None


def encode_single_token_key(clip, token_key, token_weight_pairs):
    cond, pooled = clip.encode_from_tokens({token_key: token_weight_pairs}, return_pooled=True)
    return cond, pooled


def prepare_xl(embeddings_l, embeddings_g, pooled, clip_balance=0.5):
    weight_l = 1 - max(0, clip_balance - 0.5) * 2
    weight_g = 1 - max(0, 0.5 - clip_balance) * 2
    if embeddings_l is not None:
        return torch.cat([embeddings_l * weight_l, embeddings_g * weight_g], dim=-1), pooled
    return embeddings_g, pooled


def advanced_encode(clip, text, token_normalization, weight_interpretation, tokenize_func, apply_to_pooled=True):
    tokenized = tokenize_func(text, return_word_ids=True)
    cond_model = clip.cond_stage_model
    if SD3ClipModel is not None and isinstance(cond_model, SD3ClipModel):
        lg_out = None
        pooled = None
        out = None
        if "l" in tokenized and "g" in tokenized and (len(tokenized["l"]) > 0 or len(tokenized["g"]) > 0):
            if cond_model.clip_l is not None:
                lg_out, l_pooled = advanced_encode_from_tokens(tokenized["l"], token_normalization, weight_interpretation, lambda x: encode_token_weights_for_model(clip, x, encode_token_weights_l), return_pooled=True)
            else:
                l_pooled = torch.zeros((1, 768), device=model_management.intermediate_device())
            if cond_model.clip_g is not None:
                g_out, g_pooled = advanced_encode_from_tokens(tokenized["g"], token_normalization, weight_interpretation, lambda x: encode_token_weights_for_model(clip, x, encode_token_weights_g), return_pooled=True)
                if lg_out is not None:
                    cut_to = min(lg_out.shape[1], g_out.shape[1])
                    lg_out = torch.cat([lg_out[:, :cut_to], g_out[:, :cut_to]], dim=-1)
                else:
                    lg_out = torch.nn.functional.pad(g_out, (768, 0))
            else:
                g_pooled = torch.zeros((1, 1280), device=model_management.intermediate_device())
            if lg_out is not None:
                lg_out = torch.nn.functional.pad(lg_out, (0, 4096 - lg_out.shape[-1]))
                out = lg_out
            pooled = torch.cat((l_pooled, g_pooled), dim=-1)
        if "t5xxl" in tokenized and cond_model.t5xxl is not None:
            t5_out, t5_pooled = advanced_encode_from_tokens(tokenized["t5xxl"], token_normalization, weight_interpretation, lambda x: encode_token_weights_for_model(clip, x, encode_token_weights_t5), return_pooled=True)
            if lg_out is not None:
                out = torch.cat([lg_out, t5_out], dim=-2)
            else:
                out = t5_out
        if out is None:
            out = torch.zeros((1, 77, 4096), device=model_management.intermediate_device())
        if pooled is None:
            pooled = torch.zeros((1, 768 + 1280), device=model_management.intermediate_device())
        return [[out, {"pooled_output": pooled}]]
    if isinstance(cond_model, (SDXLClipModel, SDXLRefinerClipModel, SDXLClipG)):
        embeddings_l = None
        embeddings_g = None
        pooled = None
        if "l" in tokenized and isinstance(cond_model, SDXLClipModel):
            embeddings_l, _ = advanced_encode_from_tokens(tokenized["l"], token_normalization, weight_interpretation, lambda x: encode_token_weights_for_model(clip, x, encode_token_weights_l))
        if "g" in tokenized:
            embeddings_g, pooled = advanced_encode_from_tokens(tokenized["g"], token_normalization, weight_interpretation, lambda x: encode_token_weights_for_model(clip, x, encode_token_weights_g), return_pooled=True, apply_to_pooled=apply_to_pooled)
        embeddings_final, pooled = prepare_xl(embeddings_l, embeddings_g, pooled)
        return [[embeddings_final, {"pooled_output": pooled}]]
    if len(tokenized) == 1:
        token_key = next(iter(tokenized))
        embeddings_final, pooled = advanced_encode_from_tokens(tokenized[token_key], token_normalization, weight_interpretation, lambda x: encode_single_token_key(clip, token_key, x), return_pooled=True)
        return [[embeddings_final, {"pooled_output": pooled}]]
    tokens = tokenize_func(text)
    return clip.encode_from_tokens_scheduled(tokens)

KREA2_TEMPLATE = "<|im_start|>system\nDescribe the image by detailing the color, shape, size, texture, quantity, text, spatial relationships of the objects and background:<|im_end|>\n<|im_start|>user\n{}<|im_end|>\n<|im_start|>assistant\n"

class SwarmClipTextEncodeAdvanced:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "clip": ("CLIP", ),
                "steps": ("INT", {"default": 20, "min": 1, "max": 10000, "tooltip": "How many sampling steps will be ran - this is needed for per-step features (from-to/alternate/...) to work properly."}),
                "prompt": ("STRING", {"multiline": True, "dynamicPrompts": True, "tooltip": "Your actual prompt text."} ),
                "width": ("INT", {"default": 1024.0, "min": 0, "max": MAX_RESOLUTION, "tooltip": "Intended width of the image, used by some models (eg SDXL)."}),
                "height": ("INT", {"default": 1024.0, "min": 0, "max": MAX_RESOLUTION, "tooltip": "Intended height of the image, used by some models (eg SDXL)."}),
                "target_width": ("INT", {"default": 1024.0, "min": 0, "max": MAX_RESOLUTION, "tooltip": "Actual width of the image, used by some models (eg SDXL)."}),
                "target_height": ("INT", {"default": 1024.0, "min": 0, "max": MAX_RESOLUTION, "tooltip": "Actual height of the image, used by some models (eg SDXL)."}),
            },
            "optional": {
                "guidance": ("FLOAT", {"default": -1, "min": -1, "max": 100.0, "step": 0.1, "tooltip": "Guidance value to embed, used by some models (eg Flux)."}),
                "llama_template": ("STRING", {"default": "", "multiline": True, "tooltip": "Template for the LLaMA model, if applicable."}),
                "clip_vision_output": ("CLIP_VISION_OUTPUT", {"default": None, "tooltip": "Optional CLIP Vision Output to use for the LLaMA model, if applicable."}),
                "images": ("IMAGE", {"default": None, "tooltip": "Optional images to use for a text-vision model, if applicable."}),
                "token_normalization": (["none", "mean", "length", "length+mean"], {"default": "none", "tooltip": "How prompt weights should be normalized across tokens."}),
                "weight_interpretation": (["comfy", "A1111", "compel", "comfy++", "down_weight"], {"default": "comfy", "tooltip": "How prompt weighting syntax should be interpreted."}),
            }
        }

    CATEGORY = "SwarmUI/clip"
    RETURN_TYPES = ("CONDITIONING",)
    FUNCTION = "encode"
    DESCRIPTION = "Acts like the regular CLIPTextEncode, but supports more advanced special features like '<break>', '[from:to:when]', '[alter|nate]', ..."

    def encode(self, clip, steps: int, prompt: str, width: int, height: int, target_width: int, target_height: int, guidance: float = -1, llama_template = None, clip_vision_output = None, images = None, token_normalization = "none", weight_interpretation = "comfy"):
        append_images = False
        prepend_images = False
        fix_images = True
        if llama_template == "hunyuan_image":
            llama_template = PROMPT_TEMPLATE_ENCODE_VIDEO_I2V
            fix_images = False
        elif llama_template == "krea2":
            llama_template = KREA2_TEMPLATE
            append_images = True
        elif llama_template == "qwen_image_edit_plus":
            llama_template = PROMPT_TEMPLATE_QWEN_IMAGE_EDIT_PLUS
            append_images = True
            prepend_images = True
        if images is not None and fix_images:
            if len(images.shape) == 3:
                images = [images]
            else:
                images = [i.unsqueeze(0) for i in images]

        def tokenize(text: str, return_word_ids = False):
            if clip_vision_output is not None:
                return clip.tokenize(text, return_word_ids=return_word_ids, llama_template=llama_template if llama_template else None, image_embeds=clip_vision_output.mm_projected)
            elif images is not None:
                if append_images:
                    image_prompt = ""
                    for i, image in enumerate(images):
                        if f"input_image_{i + 1}" in text:
                            text = text.replace(f"input_image_{i + 1}", f"<|vision_start|><|image_pad|><|vision_end|>", 1)
                        else:
                            image_prompt += f"Picture {i + 1}: <|vision_start|><|image_pad|><|vision_end|>"
                    if prepend_images:
                        text = image_prompt + text
                    else:
                        text = text + image_prompt
                return clip.tokenize(text, return_word_ids=return_word_ids, llama_template=llama_template if llama_template else None, images=images)
            else:
                return clip.tokenize(text, return_word_ids=return_word_ids)

        encoding_cache = {}

        def text_to_cond(text: str, start_percent: float, end_percent: float):
            text = text.replace("\0\1", "[").replace("\0\2", "]").replace("\0\3", "embedding:")
            if text in encoding_cache:
                cond_arr = encoding_cache[text]
            else:
                cond_chunks = text.split("<break>")
                if token_normalization == "none" and weight_interpretation == "comfy":
                    tokens = tokenize(cond_chunks[0])
                    cond_arr = clip.encode_from_tokens_scheduled(tokens)
                else:
                    cond_arr = advanced_encode(clip, cond_chunks[0], token_normalization, weight_interpretation, tokenize)
                if len(cond_chunks) > 1:
                    for chunk in cond_chunks[1:]:
                        if token_normalization == "none" and weight_interpretation == "comfy":
                            tokens = tokenize(chunk)
                            cond_arr_chunk = clip.encode_from_tokens_scheduled(tokens)
                        else:
                            cond_arr_chunk = advanced_encode(clip, chunk, token_normalization, weight_interpretation, tokenize)
                        catted_cond = torch.cat([cond_arr[0][0], cond_arr_chunk[0][0]], dim=1)
                        cond_arr[0] = [catted_cond, cond_arr[0][1]]
                encoding_cache[text] = cond_arr
            result = {"pooled_output": cond_arr[0][1]["pooled_output"], "width": width, "height": height, "crop_w": 0, "crop_h": 0, "target_width": target_width, "target_height": target_height, "start_percent": start_percent, "end_percent": end_percent}
            for k, v in cond_arr[0][1].items():
                if k not in result:
                    result[k] = v
            if guidance >= 0:
                result["guidance"] = guidance
            out_cond_arr = [[cond_arr[0][0], result]]
            out_cond_arr.extend(cond_arr[1:])
            return out_cond_arr

        prompt = prompt.replace("\\[", "\0\1").replace("\\]", "\0\2").replace("embedding:", "\0\3")

        chunks = []
        any = [False]
        escapable = ["\\", "[", "]", ":", "|", "(", ")", "<", ">"]

        def append_chunk(text: str, applies_to: list[int], can_subprocess: bool, limit_to: list[int]):
            applies_to = [i for i in applies_to if i in limit_to]
            fixed_text = ""
            do_skip = False
            for i in range(len(text)):
                if text[i] == "\\" and not do_skip and i + 1 < len(text) and text[i + 1] in escapable:
                    do_skip = True
                else:
                    do_skip = False
                    fixed_text += text[i]
            if can_subprocess and '[' in fixed_text:
                get_chunks(fixed_text, applies_to)
            else:
                chunks.append({'text': text, 'applies_to': applies_to})

        def get_chunks(remaining: str, limit_to: list[int] = [i for i in range(steps)]):
            while True:
                start = remaining.find("[")
                if start == -1:
                    append_chunk(remaining, [i for i in range(steps)], False, limit_to)
                    break

                end = -1
                count = 0
                do_skip = False
                colon_indices = []
                pipe_indices = []
                for i in range(start + 1, len(remaining)):
                    char = remaining[i]
                    if char == "\\" and not do_skip and i + 1 < len(remaining) and remaining[i + 1] in escapable:
                        do_skip = True
                    elif do_skip:
                        do_skip = False
                    elif char == "[":
                        count += 1
                    elif char == "]":
                        if count == 0:
                            end = i
                            break
                        count -= 1
                    elif char == ":" and count == 0 and len(pipe_indices) == 0:
                        colon_indices.append(i)
                    elif char == "|" and count == 0 and len(colon_indices) == 0:
                        pipe_indices.append(i)

                if count != 0 or (end == -1 and len(chunks) == 0):
                    append_chunk(remaining, [i for i in range(steps)], False, limit_to)
                    break
                if end == -1:
                    chunks[-1].text += remaining
                    break
                append_chunk(remaining[:start], [i for i in range(steps)], False, limit_to)
                control = remaining[start + 1:end]

                if len(pipe_indices) > 0:
                    data = split_text_on(control, pipe_indices, start + 1)
                    for i in range(len(data)):
                        append_chunk(data[i], [step for step in range(steps) if step % len(data) == i], True, limit_to)
                    any[0] = True
                elif len(colon_indices) == 2:
                    coloned = split_text_on(control, colon_indices, start + 1)
                    when = float(coloned[2])
                    if when < 1:
                        when = when * steps
                    append_chunk(coloned[0], [i for i in range(steps) if i < when], True, limit_to)
                    append_chunk(coloned[1], [i for i in range(steps) if i >= when], True, limit_to)
                    any[0] = True
                elif len(colon_indices) == 1:
                    coloned = split_text_on(control, colon_indices, start + 1)
                    when = float(coloned[1])
                    if when < 1:
                        when = when * steps
                    append_chunk(coloned[0], [i for i in range(steps) if i >= when], True, limit_to)
                    any[0] = True
                else:
                    append_chunk(control, [i for i in range(steps)], False, limit_to)

                remaining = remaining[end + 1:]

        get_chunks(prompt)

        if not any[0]:
            return (text_to_cond(prompt, 0, 1), )

        conds_out = []
        last_text = ""
        start_perc = 0
        for i in range(steps):
            perc = i / steps
            text = ""
            for chunk in chunks:
                if i in chunk['applies_to']:
                    text += chunk['text']
            if text != last_text or i == 0:
                if i != 0:
                    conds_out.extend(text_to_cond(last_text, start_perc - 0.001, perc + 0.001))
                last_text = text
                start_perc = perc
        conds_out.extend(text_to_cond(last_text, start_perc - 0.001, 1))
        return (conds_out, )


def split_text_on(text: str, indices: list[str], offset: int) -> list[str]:
    indices = [i - offset for i in indices]
    result = []
    result.append(text[:indices[0]])
    for i in range(len(indices) - 1):
        result.append(text[indices[i] + 1:indices[i + 1]])
    result.append(text[indices[-1] + 1:])
    return result


NODE_CLASS_MAPPINGS = {
    "SwarmClipTextEncodeAdvanced": SwarmClipTextEncodeAdvanced,
}
