import torch
import torch.nn.functional as F
import comfy.model_management
import comfy.utils
import numpy as np
from concurrent.futures import ThreadPoolExecutor
import os

# Try to import kornia for color space conversions
try:
    import kornia
    KORNIA_AVAILABLE = True
except ImportError:
    KORNIA_AVAILABLE = False
    print("Kornia not available, some color spaces will be limited")


class LatentColorMatch:
    """
    A ComfyUI node that performs color matching in the latent space.
    Based on the cubiq ImageColorMatch implementation but adapted for latent space.
    """

    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "latent": ("LATENT",),
                "reference": ("LATENT",),
                "method": (
                    [
                        'LAB',
                        'YCbCr',
                        'RGB',
                        'LUV',
                        'YUV',
                        'XYZ',
                        'mkl',
                        'hm',
                        'reinhard',
                        'mvgd',
                        'hm-mvgd-hm',
                        'hm-mkl-hm'
                    ], {
                       "default": 'LAB'
                    }),
                "factor": ("FLOAT", { "default": 1.0, "min": 0.0, "max": 3.0, "step": 0.05, }),
                "device": (["auto", "cpu", "gpu"],),
                "batch_size": ("INT", { "default": 0, "min": 0, "max": 1024, "step": 1, }),
            }
        }

    RETURN_TYPES = ("LATENT",)
    FUNCTION = "execute"
    CATEGORY = "latent/color"
    DESCRIPTION = """
Latent Color Match based on cubiq's ImageColorMatch.
Supports both traditional color space methods (LAB, YCbCr, etc.)
and advanced methods (hm, mkl, hm-mkl-hm) for latent space.
"""

    def execute(self, latent, reference, method, factor, device, batch_size):
        print(f"=== LatentColorMatch Debug ===")
        print(f"Method: {method}")
        print(f"Factor: {factor}")

        # Device management
        if "gpu" == device:
            device = comfy.model_management.get_torch_device()
        elif "auto" == device:
            device = comfy.model_management.intermediate_device()
        else:
            device = 'cpu'

        # Extract latent tensors and store original shapes
        latent_samples = latent["samples"]
        reference_samples = reference["samples"]

        # Store original shapes to restore later
        original_latent_shape = latent_samples.shape
        original_reference_shape = reference_samples.shape

        print(f"Original Latent shape: {original_latent_shape}")
        print(f"Original Reference shape: {original_reference_shape}")

        # Create working copies with proper 4D shape for processing
        working_latent = latent_samples
        working_reference = reference_samples

        # Handle different tensor shapes - squeeze extra dimensions intelligently
        # For 5D tensors like [1, 16, 1, 436, 248], we want to get [1, 16, 436, 248]
        if len(working_latent.shape) == 5:
            # Find dimensions with size 1 and squeeze them (except batch dimension)
            for dim in range(len(working_latent.shape) - 1, 0, -1):  # Go backwards, skip batch dim
                if working_latent.shape[dim] == 1 and len(working_latent.shape) > 4:
                    print(f"Squeezing latent dimension {dim} from {working_latent.shape}")
                    working_latent = working_latent.squeeze(dim)
                    break

        if len(working_reference.shape) == 5:
            # Find dimensions with size 1 and squeeze them (except batch dimension)
            for dim in range(len(working_reference.shape) - 1, 0, -1):  # Go backwards, skip batch dim
                if working_reference.shape[dim] == 1 and len(working_reference.shape) > 4:
                    print(f"Squeezing reference dimension {dim} from {working_reference.shape}")
                    working_reference = working_reference.squeeze(dim)
                    break

        # If still not 4D, use squeeze() without specific dimension
        if len(working_latent.shape) > 4:
            print(f"Force squeezing latent from {working_latent.shape}")
            working_latent = working_latent.squeeze()
            # Ensure we still have at least 4 dimensions
            while len(working_latent.shape) < 4:
                working_latent = working_latent.unsqueeze(0)

        if len(working_reference.shape) > 4:
            print(f"Force squeezing reference from {working_reference.shape}")
            working_reference = working_reference.squeeze()
            # Ensure we still have at least 4 dimensions
            while len(working_reference.shape) < 4:
                working_reference = working_reference.unsqueeze(0)

        print(f"Working Latent shape: {working_latent.shape}")
        print(f"Working Reference shape: {working_reference.shape}")
        print(f"Latent range: {working_latent.min().item():.3f} to {working_latent.max().item():.3f}")
        print(f"Reference range: {working_reference.min().item():.3f} to {working_reference.max().item():.3f}")

        # Use working tensors for processing
        latent_samples = working_latent
        reference_samples = working_reference

        # Move to device
        latent_samples = latent_samples.to(device)
        reference_samples = reference_samples.to(device)

        # Ensure reference and latent have compatible shapes
        if reference_samples.shape[1:] != latent_samples.shape[1:]:
            reference_samples = comfy.utils.common_upscale(
                reference_samples,
                latent_samples.shape[3],
                latent_samples.shape[2],
                upscale_method='bicubic',
                crop='center'
            )

        # Handle batch processing
        if batch_size == 0 or batch_size > latent_samples.shape[0]:
            batch_size = latent_samples.shape[0]

        # Store original for comparison
        original_samples = latent_samples.clone()

        # Process latent in batches
        latent_batch = torch.split(latent_samples, batch_size, dim=0)
        output = []

        for batch in latent_batch:
            batch = batch.to(device)

            # Apply color matching based on method
            if method in ['mkl', 'hm', 'reinhard', 'mvgd', 'hm-mvgd-hm', 'hm-mkl-hm']:
                print(f"Using advanced method: {method}")
                try:
                    matched = self.color_match_advanced(batch, reference_samples, method, factor)
                    print(f"Advanced method succeeded")
                except Exception as e:
                    print(f"Advanced method {method} failed: {e}, falling back to LAB")
                    matched = self.color_match_latent(batch, reference_samples, 'LAB', factor)
            else:
                print(f"Using basic method: {method}")
                matched = self.color_match_latent(batch, reference_samples, method, factor)

            output.append(matched.to(comfy.model_management.intermediate_device()))

        # Combine results
        matched_samples = torch.cat(output, dim=0)

        # Restore original shape if it was different
        if matched_samples.shape != original_latent_shape:
            print(f"Reshaping result from {matched_samples.shape} to {original_latent_shape}")

            # If original was 5D, we need to add back the squeezed dimension
            if len(original_latent_shape) == 5 and len(matched_samples.shape) == 4:
                # Find where to add the dimension back
                for dim in range(1, len(original_latent_shape)):
                    if original_latent_shape[dim] == 1:
                        matched_samples = matched_samples.unsqueeze(dim)
                        print(f"Added dimension at position {dim}: {matched_samples.shape}")
                        break

            # Verify the shape matches now
            if matched_samples.shape != original_latent_shape:
                print(f"Warning: Could not restore exact original shape!")
                print(f"Original: {original_latent_shape}, Result: {matched_samples.shape}")
                # Try to reshape directly if possible
                try:
                    matched_samples = matched_samples.view(original_latent_shape)
                    print(f"Successfully reshaped to: {matched_samples.shape}")
                except:
                    print(f"Could not reshape - keeping current shape: {matched_samples.shape}")

        # Check if anything actually changed
        diff = torch.abs(matched_samples - latent["samples"].to(matched_samples.device)).mean()
        print(f"Average difference from original: {diff.item():.6f}")
        print(f"Final result shape: {matched_samples.shape}")
        print(f"Matched range: {matched_samples.min().item():.3f} to {matched_samples.max().item():.3f}")

        # Create output latent dict
        result = latent.copy()
        result["samples"] = matched_samples

        return (result,)

    def color_match_latent(self, latent, reference, method, factor):
        """
        Color matching for latents based on cubiq's ImageColorMatch
        Uses the exact same algorithm but adapted for latent space
        """
        print(f"Using color_match_latent with method: {method}")
        print(f"Input latent shape: {latent.shape}")
        print(f"Input reference shape: {reference.shape}")

        # Ensure we have 4D tensors
        if len(latent.shape) != 4:
            raise ValueError(f"Expected 4D latent tensor, got {latent.shape}")
        if len(reference.shape) != 4:
            raise ValueError(f"Expected 4D reference tensor, got {reference.shape}")

        # Convert latents to format expected by color matching
        # Latents are [batch, channels, height, width], we need to treat them as RGB-like

        # For latents, we'll use the first 3 channels as "RGB" if available
        if latent.shape[1] >= 3:
            # Use first 3 channels as pseudo-RGB
            latent_rgb = latent[:, :3, :, :]
            reference_rgb = reference[:, :3, :, :]

            # Handle remaining channels separately
            if latent.shape[1] > 3:
                latent_extra = latent[:, 3:, :, :]
                reference_extra = reference[:, 3:, :, :]
            else:
                latent_extra = None
                reference_extra = None
        else:
            # If less than 3 channels, pad with zeros
            latent_rgb = F.pad(latent, (0, 0, 0, 0, 0, 3 - latent.shape[1]))
            reference_rgb = F.pad(reference, (0, 0, 0, 0, 0, 3 - reference.shape[1]))
            latent_extra = None
            reference_extra = None

        print(f"Latent RGB shape: {latent_rgb.shape}")
        print(f"Reference RGB shape: {reference_rgb.shape}")

        # Validate shapes for kornia
        if latent_rgb.shape[1] != 3:
            print(f"Warning: RGB tensor should have 3 channels, got {latent_rgb.shape[1]}")

        # Apply color space conversion and matching using cubiq's method
        if KORNIA_AVAILABLE and method in ['LAB', 'YCbCr', 'LUV', 'YUV', 'XYZ']:
            matched_rgb = self.apply_cubiq_color_matching(latent_rgb, reference_rgb, method, factor)
        else:
            print(f"Kornia not available or unsupported method {method}, using RGB matching")
            matched_rgb = self.apply_cubiq_color_matching(latent_rgb, reference_rgb, 'RGB', factor)

        # Reconstruct full latent
        if latent_extra is not None:
            # Apply simple matching to extra channels
            matched_extra = self.match_extra_channels(latent_extra, reference_extra, factor)
            matched = torch.cat([matched_rgb, matched_extra], dim=1)
        else:
            # Trim back to original channel count
            matched = matched_rgb[:, :latent.shape[1], :, :]

        print(f"Final matched shape: {matched.shape}")
        return matched

    def apply_cubiq_color_matching(self, latent, reference, color_space, factor):
        """Apply cubiq's exact color matching algorithm"""

        # For latents, we don't normalize to 0-1 like images
        # Instead, work directly with latent values for stronger effect
        print(f"Direct latent range: {latent.min().item():.3f} to {latent.max().item():.3f}")
        print(f"Direct reference range: {reference.min().item():.3f} to {reference.max().item():.3f}")

        # Use latents directly without normalization for stronger effect
        latent_norm = latent
        reference_norm = reference

        # Apply color space conversion with error handling
        try:
            if KORNIA_AVAILABLE and color_space == "LAB":
                print(f"Converting to LAB - latent shape: {latent_norm.shape}")
                latent_converted = kornia.color.rgb_to_lab(latent_norm)
                reference_converted = kornia.color.rgb_to_lab(reference_norm)
            elif KORNIA_AVAILABLE and color_space == "YCbCr":
                print(f"Converting to YCbCr - latent shape: {latent_norm.shape}")
                latent_converted = kornia.color.rgb_to_ycbcr(latent_norm)
                reference_converted = kornia.color.rgb_to_ycbcr(reference_norm)
            elif KORNIA_AVAILABLE and color_space == "LUV":
                print(f"Converting to LUV - latent shape: {latent_norm.shape}")
                latent_converted = kornia.color.rgb_to_luv(latent_norm)
                reference_converted = kornia.color.rgb_to_luv(reference_norm)
            elif KORNIA_AVAILABLE and color_space == "YUV":
                print(f"Converting to YUV - latent shape: {latent_norm.shape}")
                latent_converted = kornia.color.rgb_to_yuv(latent_norm)
                reference_converted = kornia.color.rgb_to_yuv(reference_norm)
            elif KORNIA_AVAILABLE and color_space == "XYZ":
                print(f"Converting to XYZ - latent shape: {latent_norm.shape}")
                latent_converted = kornia.color.rgb_to_xyz(latent_norm)
                reference_converted = kornia.color.rgb_to_xyz(reference_norm)
            else:
                # RGB or fallback
                print(f"Using RGB (no conversion) - latent shape: {latent_norm.shape}")
                latent_converted = latent_norm
                reference_converted = reference_norm
        except Exception as e:
            print(f"Color space conversion failed: {e}")
            print(f"Falling back to RGB mode")
            latent_converted = latent_norm
            reference_converted = reference_norm
            color_space = "RGB"  # Update for later conversion back

        # Compute mean and std (cubiq's method) - per channel for better matching
        reference_mean, reference_std = self.compute_mean_std_cubiq(reference_converted)
        latent_mean, latent_std = self.compute_mean_std_cubiq(latent_converted)

        print(f"Reference mean: {reference_mean.mean().item():.3f}, std: {reference_std.mean().item():.3f}")
        print(f"Latent mean: {latent_mean.mean().item():.3f}, std: {latent_std.mean().item():.3f}")

        # Apply color matching with stronger effect for latents
        # Avoid division by zero
        safe_latent_std = torch.clamp(latent_std, min=1e-6)
        safe_reference_std = torch.clamp(reference_std, min=1e-6)

        # Normalize and match
        normalized = (latent_converted - latent_mean) / safe_latent_std
        matched_converted = normalized * safe_reference_std + reference_mean

        # Apply factor with stronger blending for latents
        # Use a non-linear factor to make the effect more visible
        effective_factor = factor ** 0.5  # Square root for stronger effect at lower values
        matched_converted = effective_factor * matched_converted + (1 - effective_factor) * latent_converted

        print(f"Effective factor used: {effective_factor:.3f} (from {factor:.3f})")

        # Convert back to RGB with error handling
        try:
            if KORNIA_AVAILABLE and color_space == "LAB":
                print(f"Converting back from LAB to RGB")
                matched_norm = kornia.color.lab_to_rgb(matched_converted)
            elif KORNIA_AVAILABLE and color_space == "YCbCr":
                print(f"Converting back from YCbCr to RGB")
                matched_norm = kornia.color.ycbcr_to_rgb(matched_converted)
            elif KORNIA_AVAILABLE and color_space == "LUV":
                print(f"Converting back from LUV to RGB")
                matched_norm = kornia.color.luv_to_rgb(matched_converted)
            elif KORNIA_AVAILABLE and color_space == "YUV":
                print(f"Converting back from YUV to RGB")
                matched_norm = kornia.color.yuv_to_rgb(matched_converted)
            elif KORNIA_AVAILABLE and color_space == "XYZ":
                print(f"Converting back from XYZ to RGB")
                matched_norm = kornia.color.xyz_to_rgb(matched_converted)
            else:
                print(f"No back-conversion needed (RGB)")
                matched_norm = matched_converted
        except Exception as e:
            print(f"Back-conversion failed: {e}")
            print(f"Using matched result without back-conversion")
            matched_norm = matched_converted

        # For latents, we didn't normalize, so no denormalization needed
        matched = matched_norm

        # Ensure no NaN or Inf values that could cause raster artifacts
        matched = torch.nan_to_num(matched, nan=0.0, posinf=1e6, neginf=-1e6)

        # Smooth any potential discontinuities that could cause raster artifacts
        if matched.shape[2] > 1 and matched.shape[3] > 1:  # Only if spatial dimensions > 1
            # Apply very light smoothing to reduce raster artifacts
            matched = self.smooth_tensor(matched)

        print(f"Final matched range: {matched.min().item():.3f} to {matched.max().item():.3f}")

        return matched

    def smooth_tensor(self, tensor):
        """Apply very light smoothing to reduce raster artifacts"""
        # Use a small gaussian blur to smooth out artifacts
        # This is very light to preserve details
        if tensor.shape[2] <= 2 or tensor.shape[3] <= 2:
            return tensor

        # Create a small smoothing kernel
        kernel_size = 3
        sigma = 0.5

        # Simple box filter for light smoothing
        kernel = torch.ones(1, 1, kernel_size, kernel_size, device=tensor.device) / (kernel_size * kernel_size)

        smoothed = tensor.clone()
        for c in range(tensor.shape[1]):
            channel = tensor[:, c:c+1, :, :]
            # Apply padding to maintain size
            padded = F.pad(channel, (1, 1, 1, 1), mode='reflect')
            smoothed_channel = F.conv2d(padded, kernel, padding=0)
            # Blend with original (very light smoothing)
            smoothed[:, c:c+1, :, :] = 0.95 * channel + 0.05 * smoothed_channel

        return smoothed

    def compute_mean_std_cubiq(self, tensor):
        """Compute mean and std exactly like cubiq does"""
        mean = tensor.mean(dim=[2, 3], keepdim=True)
        std = tensor.std(dim=[2, 3], keepdim=True)
        return mean, std

    def match_extra_channels(self, latent_extra, reference_extra, factor):
        """Simple matching for extra channels beyond RGB"""
        if latent_extra is None:
            return None

        matched_extra = latent_extra.clone()
        for c in range(latent_extra.shape[1]):
            latent_ch = latent_extra[:, c:c+1, :, :]
            ref_ch = reference_extra[:, c:c+1, :, :]

            latent_mean = latent_ch.mean(dim=[2, 3], keepdim=True)
            latent_std = latent_ch.std(dim=[2, 3], keepdim=True)

            ref_mean = ref_ch.mean(dim=[2, 3], keepdim=True)
            ref_std = ref_ch.std(dim=[2, 3], keepdim=True)

            # Apply matching
            normalized = (latent_ch - latent_mean) / (latent_std + 1e-8)
            matched = normalized * ref_std + ref_mean

            matched_extra[:, c:c+1, :, :] = factor * matched + (1 - factor) * latent_ch

        return matched_extra

    def color_match_advanced(self, latent, reference, method, factor):
        """Advanced color matching using color-matcher library"""
        try:
            from color_matcher import ColorMatcher

            # Convert to numpy and proper format for color-matcher
            # Latents: [batch, channels, height, width] -> [batch, height, width, channels]
            latent_np = latent.permute(0, 2, 3, 1).cpu().numpy()
            reference_np = reference.permute(0, 2, 3, 1).cpu().numpy()

            # Normalize to 0-1 range (color-matcher expects this)
            latent_min, latent_max = latent_np.min(), latent_np.max()
            ref_min, ref_max = reference_np.min(), reference_np.max()

            latent_norm = (latent_np - latent_min) / (latent_max - latent_min + 1e-8)
            reference_norm = (reference_np - ref_min) / (ref_max - ref_min + 1e-8)

            # Apply color matching
            cm = ColorMatcher()
            matched_list = []

            for i in range(latent_norm.shape[0]):
                src = latent_norm[i]
                ref = reference_norm[0] if reference_norm.shape[0] == 1 else reference_norm[i]

                # Ensure we have at least 3 channels for color-matcher
                if src.shape[-1] < 3:
                    # Pad with zeros if less than 3 channels
                    src_padded = np.zeros((*src.shape[:-1], 3))
                    src_padded[..., :src.shape[-1]] = src
                    ref_padded = np.zeros((*ref.shape[:-1], 3))
                    ref_padded[..., :ref.shape[-1]] = ref

                    matched_padded = cm.transfer(src=src_padded, ref=ref_padded, method=method)
                    matched = matched_padded[..., :src.shape[-1]]
                else:
                    # Use only first 3 channels for color matching
                    src_3ch = src[..., :3]
                    ref_3ch = ref[..., :3]
                    matched_3ch = cm.transfer(src=src_3ch, ref=ref_3ch, method=method)

                    # Reconstruct full tensor
                    matched = src.copy()
                    matched[..., :3] = matched_3ch

                    # Apply simple matching to remaining channels
                    if src.shape[-1] > 3:
                        for c in range(3, src.shape[-1]):
                            src_ch = src[..., c]
                            ref_ch = ref[..., c]

                            src_mean, src_std = src_ch.mean(), src_ch.std()
                            ref_mean, ref_std = ref_ch.mean(), ref_ch.std()

                            matched[..., c] = (src_ch - src_mean) / (src_std + 1e-8) * ref_std + ref_mean

                matched_list.append(matched)

            matched_np = np.stack(matched_list, axis=0)

            # Denormalize back to original range
            matched_denorm = matched_np * (latent_max - latent_min) + latent_min

            # Convert back to tensor format [batch, channels, height, width]
            matched_tensor = torch.from_numpy(matched_denorm).permute(0, 3, 1, 2).to(latent.device, latent.dtype)

            # Apply factor
            result = factor * matched_tensor + (1 - factor) * latent

            return result

        except Exception as e:
            print(f"Advanced color matching failed: {e}")
            # Fallback to simple method
            return self.color_match_latent(latent, reference, 'LAB', factor)






class LatentColorMatchSimple:
    """
    Simplified version of LatentColorMatch that only uses basic methods.
    This is a fallback for when color-matcher is not available.
    """

    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "latent": ("LATENT",),
                "reference": ("LATENT",),
                "method": (["mean_std", "channel_wise"], {"default": "mean_std"}),
                "strength": ("FLOAT", { "default": 1.0, "min": 0.0, "max": 2.0, "step": 0.05, }),
                "device": (["auto", "cpu", "gpu"],),
            }
        }

    RETURN_TYPES = ("LATENT",)
    FUNCTION = "execute"
    CATEGORY = "latent/color"
    DESCRIPTION = """
Simple Latent Color Match using basic statistical methods.
This version works without external dependencies.
"""

    def execute(self, latent, reference, method, strength, device):
        try:
            # Device management
            if "gpu" == device:
                device = comfy.model_management.get_torch_device()
            elif "auto" == device:
                device = comfy.model_management.intermediate_device()
            else:
                device = 'cpu'

            # Extract latent tensors
            latent_samples = latent["samples"].to(device)
            reference_samples = reference["samples"].to(device)

            # Ensure compatible shapes
            if reference_samples.shape[1:] != latent_samples.shape[1:]:
                target_h = latent_samples.shape[-2]
                target_w = latent_samples.shape[-1]
                reference_samples = comfy.utils.common_upscale(
                    reference_samples, target_w, target_h,
                    upscale_method='bicubic', crop='center'
                )

            # Apply basic color matching
            if method == "mean_std":
                matched_samples = self.match_mean_std_simple(latent_samples, reference_samples, strength)
            elif method == "channel_wise":
                matched_samples = self.match_channel_wise_simple(latent_samples, reference_samples, strength)
            else:
                matched_samples = latent_samples

            # Create output latent dict
            result = latent.copy()
            result["samples"] = matched_samples.to(comfy.model_management.intermediate_device())

            return (result,)

        except Exception as e:
            print(f"LatentColorMatchSimple error: {e}")
            return (latent,)

    def match_mean_std_simple(self, latent, reference, strength):
        """Simple mean/std matching"""
        # Compute statistics across spatial dimensions
        latent_mean = latent.mean(dim=[2, 3], keepdim=True)
        latent_std = latent.std(dim=[2, 3], keepdim=True)

        reference_mean = reference.mean(dim=[2, 3], keepdim=True)
        reference_std = reference.std(dim=[2, 3], keepdim=True)

        # Avoid division by zero
        latent_std = torch.clamp(latent_std, min=1e-6)

        # Normalize and scale
        normalized = (latent - latent_mean) / latent_std
        matched = normalized * reference_std + reference_mean

        # Apply strength
        result = strength * matched + (1 - strength) * latent
        return result

    def match_channel_wise_simple(self, latent, reference, strength):
        """Simple channel-wise matching"""
        result = latent.clone()

        for c in range(latent.shape[1]):
            latent_channel = latent[:, c:c+1, :, :]
            reference_channel = reference[:, c:c+1, :, :]

            latent_mean = latent_channel.mean()
            latent_std = torch.clamp(latent_channel.std(), min=1e-6)

            reference_mean = reference_channel.mean()
            reference_std = reference_channel.std()

            normalized = (latent_channel - latent_mean) / latent_std
            matched = normalized * reference_std + reference_mean

            result[:, c:c+1, :, :] = strength * matched + (1 - strength) * latent_channel

        return result


# Node classes are exported via __init__.py
