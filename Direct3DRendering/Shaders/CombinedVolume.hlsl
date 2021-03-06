﻿// https://developer.amd.com/wordpress/media/2012/10/Chapter9-Real-Time_Isosurface_Extraction.pdf

static const uint maxNumVolumes = 8;

Texture2D<float>		txPositionFront				: register(t0);
Texture2D<float>		txPositionBack				: register(t1);
Texture3D<bool>			txActiveVoxels				: register(t2);
Texture3D<bool>			txClippedVoxels				: register(t3);
Texture3D<float>		txVolumes[maxNumVolumes]	: register(t4);

SamplerState			samplerLinear				: register(s0);

cbuffer RenderParams : register(b0)
{
	float4x4	WorldProjView;				//64 x 1 =  64
	float2		InvWindowSize;				// 8 x 1 =   8
	float		Brightness;					// 4 x 1 =   4
	float		ClipDistance;				// 4 x 1 =   4
	float3		CameraPosition;				//12 x 1 =  12
	float		r_padding0;					// 4 x 1 =   4
	float3		CameraDirection;			//12 x 1 =  12
	float		r_padding1;					// 4 x 1 =   4
	float3		CameraUp;					//12 x 1 =  12
	float		r_padding2;					// 4 x 1 =   4
	float4		NearClipPlane;				//16 x 1 =  16
	float4		FarClipPlane;				//16 x 1 =  16
	float4		MinClipCoords;				//16 x 1 =  16
	float4		MaxClipCoords;				//16 x 1 =  16
}

cbuffer CombinedVolumeParams : register(b1)
{
	float4		IsosurfaceColor[8];			//16 x 8 = 128
	float		NumIsosurfaces;				// 4 x 1 =   4
	float		i_padding0;					// 4 x 1 =   4
	float		i_padding1;					// 4 x 1 =   4
	float		i_padding2;					// 4 x 1 =   4
}

struct ISOSURFACE_VS_Input{

};

struct ISOSURFACE_VS_Output {

};

struct RAYCAST_VS_Input {

};

struct RAYCAST_VS_Output {

};

ISOSURFACE_VS_Output ISOSURFACE_VS(ISOSURFACE_VS_Input input) {
	ISOSURFACE_VS_Output output = (ISOSURFACE_VS_Output)0;

}