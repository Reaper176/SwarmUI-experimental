"""Small Comfy extension providing a checked model-upscale operation."""
class StrataBoundedImageUpscale:
    @classmethod
    def INPUT_TYPES(cls):
        return {'required': {'upscale_model': ('UPSCALE_MODEL',), 'image': ('IMAGE',)}}
    RETURN_TYPES=('IMAGE',)
    FUNCTION='upscale'
    CATEGORY='Strata/image'

    def upscale(self,upscale_model,image):
        import math
        from comfy_extras.nodes_upscale_model import ImageUpscaleWithModel
        factor=float(upscale_model.scale)
        height,width=image.shape[1:3]
        if not math.isfinite(factor) or factor<=0:
            raise ValueError('Upscale model reported an invalid scale')
        out_width=math.ceil(width*factor);out_height=math.ceil(height*factor)
        if max(out_width,out_height)>8192 or out_width*out_height*image.shape[0]>32_000_000:
            raise ValueError('Model intrinsic upscale exceeds8192 pixels per side or32 million pixels; use a lower-scale model or resize the source first')
        return ImageUpscaleWithModel.execute(upscale_model,image)

NODE_CLASS_MAPPINGS={'StrataBoundedImageUpscale':StrataBoundedImageUpscale}
NODE_DISPLAY_NAME_MAPPINGS={'StrataBoundedImageUpscale':'Strata bounded model upscale'}

# Swarm's detector expects the historical Masks.shape attribute. Current
# Ultralytics exposes the tensor under .data; restore that read-only alias.
try:
    from ultralytics.engine.results import Masks
    if not hasattr(Masks,'shape'):
        Masks.shape=property(lambda self:self.data.shape)
except ImportError:
    pass


class StrataAttentionCouple:
    @classmethod
    def INPUT_TYPES(cls):
        return {'required': {'model': ('MODEL',), 'base_cond': ('CONDITIONING',),
                             'base_mask': ('MASK',), 'regions_json': ('STRING', {'default':'[]'})},
                'optional': {key: (kind,) for i in range(1,9)
                             for key,kind in ((f'cond_{i}','CONDITIONING'),(f'mask_{i}','MASK'))}}
    RETURN_TYPES=('MODEL',)
    FUNCTION='patch'
    CATEGORY='Strata/model'

    def patch(self,model,base_cond,base_mask,regions_json,**kwargs):
        import nodes
        from comfy.model_base import CosmosPredict2
        if not isinstance(model.model,CosmosPredict2):
            return nodes.NODE_CLASS_MAPPINGS['SwarmAttentionCouple']().patch(
                model,base_cond,base_mask,regions_json,**kwargs)
        inputs={'model':model,'base_cond':base_cond,'base_mask':base_mask}
        pairs=[(kwargs.get(f'cond_{i}'),kwargs.get(f'mask_{i}')) for i in range(1,9)]
        pairs=[(cond,mask) for cond,mask in pairs if cond is not None and mask is not None]
        if not pairs:return (model.clone(),)
        for i,(cond,mask) in enumerate(pairs,1):
            inputs[f'cond_{i}']=cond;inputs[f'mask_{i}']=mask
        # Anima uses Swarm's native implementation; explicit Cosmos workflows
        # retain the PPM implementation.
        return nodes.NODE_CLASS_MAPPINGS['AttentionCouplePPM'].execute(**inputs)

NODE_CLASS_MAPPINGS['StrataAttentionCouple']=StrataAttentionCouple
NODE_DISPLAY_NAME_MAPPINGS['StrataAttentionCouple']='Strata Anima attention couple'


def sam3_normal_tensor_loader(loader):
    """Persistent parameters must retain version counters across GPU offloading."""
    import functools
    import torch
    @functools.wraps(loader)
    def load(*args, **kwargs):
        with torch.inference_mode(False), torch.no_grad():
            return loader(*args, **kwargs)
    load._strata_normal_parameters = True
    return load


async def install_sam3_normal_parameters(app):
    import importlib
    import nodes
    grounding = nodes.NODE_CLASS_MAPPINGS.get('SAM3Grounding')
    if grounding is None:
        return
    module_name = grounding.__module__.rsplit('.', 1)[0] + '._model_cache'
    # Only the native SAM3 implementation has this model cache.
    if not grounding.__module__.endswith('.nodes.segmentation'):
        return
    cache = importlib.import_module(module_name)
    if not getattr(cache.get_or_build_model, '_strata_normal_parameters', False):
        cache.get_or_build_model = sam3_normal_tensor_loader(cache.get_or_build_model)


try:
    from server import PromptServer
    PromptServer.instance.app.on_startup.append(install_sam3_normal_parameters)
except ImportError:
    pass
